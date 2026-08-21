using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Features.Picks;

/// <summary>
/// Prices a slip the user built, using the same audited numbers the generated
/// board uses.
/// </summary>
/// <remarks>
/// The whole reason this runs on the server is the joint probability. Legs on
/// different fixtures are treated as independent, which is a fair
/// approximation. Legs on the SAME fixture are not independent, and the only
/// correlated pair the pipeline actually measures is BTTS with Over 2.5, whose
/// true joint comes off the Dixon-Coles score matrix. Any other same-fixture
/// pairing is refused rather than approximated: a plausible wrong edge is worse
/// than none, because it is a number somebody might stake against.
/// </remarks>
public sealed class PriceCustomTicketHandler(
    IApplicationDbContext dbContext,
    IOptions<ConfluenceOptions> confluenceOptions)
    : IRequestHandler<PriceCustomTicketQuery, PriceCustomTicketResponse>
{
    public async Task<PriceCustomTicketResponse> Handle(
        IReceiveContext<PriceCustomTicketQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var lang = string.IsNullOrWhiteSpace(query.Language) ? "en" : query.Language;
        var legs = query.Legs;

        if (legs.Count == 0)
            return PriceCustomTicketResponse.Fail("A ticket needs at least one leg.");

        if (legs.Count > PriceCustomTicketQueryValidator.MaxLegs)
            return PriceCustomTicketResponse.Fail(
                $"A ticket may have at most {PriceCustomTicketQueryValidator.MaxLegs} legs; {legs.Count} were sent.");

        var duplicate = legs
            .GroupBy(l => (l.FixtureId, Market: l.Market?.Trim().ToLowerInvariant()))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return PriceCustomTicketResponse.Fail(
                $"Fixture {duplicate.Key.FixtureId} appears twice with market '{duplicate.Key.Market}'.");

        var informational = confluenceOptions.Value.InformationalOnlyMarkets;
        var informationalLeg = legs.FirstOrDefault(l =>
            informational.Contains(l.Market?.Trim().ToLowerInvariant() ?? ""));
        if (informationalLeg is not null)
            return PriceCustomTicketResponse.Fail(
                $"Market '{informationalLeg.Market}' on fixture {informationalLeg.FixtureId} is informational only "
                + "and can never be part of a bet.");

        var fixtureIds = legs.Select(l => l.FixtureId).Distinct().ToList();

        var fixtures = await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => fixtureIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f, cancellationToken);

        var snapshotJson = await dbContext.FixtureAnalyses
            .AsNoTracking()
            .Where(a => fixtureIds.Contains(a.FixtureId) && a.Lang == lang)
            .ToDictionaryAsync(a => a.FixtureId, a => a.SnapshotJson, cancellationToken);

        var snapshots = new Dictionary<int, MatchAnalysis>();
        foreach (var id in fixtureIds)
        {
            if (!fixtures.ContainsKey(id))
                return PriceCustomTicketResponse.Fail($"Fixture {id} was not found.");

            var snapshot = AnalysisSnapshotSerializer.Deserialize(snapshotJson.GetValueOrDefault(id));
            if (snapshot?.DecisionAudit is null)
                return PriceCustomTicketResponse.Fail(
                    $"Fixture {id} has no analysis yet, so its markets cannot be priced.");

            snapshots[id] = snapshot;
        }

        // ── Resolve each leg against its audited market ──
        var resolved = new List<ResolvedLeg>(legs.Count);
        foreach (var requested in legs)
        {
            var market = requested.Market?.Trim().ToLowerInvariant() ?? "";
            var snapshot = snapshots[requested.FixtureId];

            var audit = snapshot.DecisionAudit!.Markets
                .FirstOrDefault(m => string.Equals(m.Market, market, StringComparison.OrdinalIgnoreCase));

            if (audit is null)
                return PriceCustomTicketResponse.Fail(
                    $"Fixture {requested.FixtureId} has no market '{requested.Market}'.");

            var odds = OddsGuard.Sanitize(audit.Odds);
            if (odds is null)
                return PriceCustomTicketResponse.Fail(
                    $"No price is published for '{requested.Market}' on fixture {requested.FixtureId} "
                    + $"({snapshot.HomeTeam} vs {snapshot.AwayTeam}). A leg with no price is not a bet.");

            resolved.Add(new ResolvedLeg(requested.FixtureId, market, audit, odds.Value, snapshot,
                fixtures[requested.FixtureId].Date));
        }

        // ── Joint probability ──
        double jointProbability = 1.0;
        foreach (var group in resolved.GroupBy(l => l.FixtureId))
        {
            var probability = JointProbabilityFor(group.ToList(), out var error);
            if (error is not null) return PriceCustomTicketResponse.Fail(error);

            jointProbability *= probability;
        }

        var totalOdds = resolved.Aggregate(1.0, (acc, l) => acc * l.Odds);
        var opt = confluenceOptions.Value;

        var ticket = new TicketDto
        {
            Kind = resolved.Count == 1 ? TicketDto.Kinds.Single : TicketDto.Kinds.Combo,
            Legs = [.. resolved.Select(ToLegDto)],
            Priced = true,
            TotalOdds = Math.Round(totalOdds, 2),
            FairOdds = jointProbability > 0 ? Math.Round(1 / jointProbability, 2) : 0,
            Probability = Math.Round(jointProbability, 4),
            Ev = Math.Round(jointProbability * totalOdds - 1, 4),
            KellyStake = ValueMath.FractionalKelly(jointProbability, totalOdds, opt.KellyFraction),
            ContainsGoalsMarket = resolved.Any(l => opt.GoalsMarkets.Contains(l.Market)),
        };

        return new PriceCustomTicketResponse { Ticket = ticket };
    }

    /// <summary>
    /// Probability for the legs a single fixture contributes.
    /// </summary>
    /// <remarks>
    /// One leg is its own probability. Two legs are only priceable when they
    /// are BTTS and Over 2.5, because that is the one same-fixture joint the
    /// score matrix gives us. Multiplying any other pair would understate the
    /// correlation badly enough to invent edge that is not there.
    /// </remarks>
    private static double JointProbabilityFor(IReadOnlyList<ResolvedLeg> legs, out string? error)
    {
        error = null;

        if (legs.Count == 1)
            return legs[0].Audit.Probability;

        var fixtureId = legs[0].FixtureId;

        if (legs.Count > 2)
        {
            error = $"Fixture {fixtureId} has {legs.Count} legs. At most two markets from one fixture "
                  + "can be priced together, and only BTTS with Over 2.5.";
            return 0;
        }

        var markets = legs.Select(l => l.Market).OrderBy(m => m, StringComparer.Ordinal).ToList();
        var isBttsOver25 =
            markets.SequenceEqual([ConfluenceRuleEngine.Markets.Btts, ConfluenceRuleEngine.Markets.Over25]);

        if (!isBttsOver25)
        {
            error = $"Markets '{string.Join("' and '", markets)}' on fixture {fixtureId} are correlated and "
                  + "the model has no joint probability for that pair. Only BTTS with Over 2.5 can be "
                  + "combined on one fixture.";
            return 0;
        }

        var joint = legs[0].Snapshot.BttsAndOver25Probability;
        if (joint is not > 0)
        {
            error = $"Fixture {fixtureId} has no joint BTTS/Over 2.5 probability, so the pair cannot be priced.";
            return 0;
        }

        return joint.Value;
    }

    private static PickLegDto ToLegDto(ResolvedLeg leg) => new()
    {
        FixtureId = leg.FixtureId,
        KickoffUtc = leg.KickoffUtc,
        League = leg.Snapshot.League,
        Match = $"{leg.Snapshot.HomeTeam} vs {leg.Snapshot.AwayTeam}",
        Market = leg.Market,
        Selection = PickSelector.SelectionOf(leg.Audit),
        Probability = Math.Round(leg.Audit.Probability, 4),
        Odds = leg.Odds,
        Ev = Math.Round(ValueMath.Ev(leg.Audit.Probability, leg.Odds), 4),
    };

    private sealed record ResolvedLeg(
        int FixtureId,
        string Market,
        MarketRuleAudit Audit,
        double Odds,
        MatchAnalysis Snapshot,
        DateTimeOffset KickoffUtc);
}
