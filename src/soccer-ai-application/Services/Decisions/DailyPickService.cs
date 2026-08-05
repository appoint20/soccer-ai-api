using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>
/// Builds the live daily pick board.
///
/// Reads precomputed snapshots — the same artefacts the analysis endpoint
/// serves — and runs <see cref="PickSelector"/> over their decision audits.
/// Because selection happens in one shared, pure component, what this endpoint
/// publishes is by construction what the backtest measured.
///
/// A missing snapshot is recomputed once through
/// <see cref="IAnalysisPrecomputeService"/>; a fixture that still has no audit
/// is simply absent from the board rather than being guessed at.
/// </summary>
public sealed class DailyPickService(
    IApplicationDbContext dbContext,
    ILeagueTierService leagueTiers,
    IAnalysisPrecomputeService precomputeService,
    IOptions<ConfluenceOptions> confluenceOptions,
    IOptions<StrategyOptions> strategyOptions,
    ILogger<DailyPickService> logger) : IDailyPickService
{
    public async Task<DailyPickBoard> GetBoardAsync(
        DateOnly date, string lang, CancellationToken ct = default)
    {
        var fixtures = await LoadScopedFixturesAsync(date, ct);
        if (fixtures.Count == 0)
        {
            logger.LogInformation("[Picks] No in-scope fixtures on {Date}", date);
            return DailyPickBoard.Empty(date);
        }

        var teams = await LoadTeamsAsync(fixtures, ct);
        var snapshots = await LoadSnapshotsAsync(fixtures, lang, ct);

        var selections = new List<FixtureSelection>(fixtures.Count);
        var refs = new Dictionary<int, FixtureRef>(fixtures.Count);
        int analyzed = 0, priced = 0;

        foreach (var fixture in fixtures)
        {
            var snapshot = await ResolveSnapshotAsync(fixture, lang, snapshots, ct);
            if (snapshot?.DecisionAudit is null) continue;

            analyzed++;

            var reference = ToFixtureRef(fixture, snapshot, teams);
            refs[fixture.Id] = reference;

            var selection = PickSelector.Select(
                reference,
                snapshot.DecisionAudit,
                snapshot.BttsAndOver25Probability,
                confluenceOptions.Value);

            if (selection.QualifiedLegs.Count > 0 || selection.ComboEligibleLegs.Count > 0)
                priced++;

            selections.Add(selection);
        }

        var tickets = PickSelector.BuildTickets(
            selections, strategyOptions.Value, confluenceOptions.Value);

        var confidencePicks = selections
            .Select(s => s.ConfidencePick)
            .OfType<ConfidencePick>()
            .OrderByDescending(p => p.Probability)
            .Take(confluenceOptions.Value.ConfidencePicksPerDay)
            .ToList();

        logger.LogInformation(
            "[Picks] {Date}: {Fixtures} fixtures, {Analyzed} analyzed, {Priced} priced, "
            + "{Tickets} tickets, {Confidence} confidence picks",
            date, fixtures.Count, analyzed, priced, tickets.Count, confidencePicks.Count);

        return new DailyPickBoard(
            date,
            tickets,
            confidencePicks,
            refs,
            new PickCoverage(fixtures.Count, analyzed, priced));
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private async Task<List<Fixture>> LoadScopedFixturesAsync(DateOnly date, CancellationToken ct)
    {
        var startUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = startUtc.AddDays(1);
        var scopedLeagueIds = leagueTiers.GetSyncLeagueIds().ToList();

        return await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => f.Date >= startUtc && f.Date < endUtc && scopedLeagueIds.Contains(f.LeagueId))
            .OrderBy(f => f.Date)
            .ToListAsync(ct);
    }

    private async Task<Dictionary<int, Team>> LoadTeamsAsync(
        IReadOnlyCollection<Fixture> fixtures, CancellationToken ct)
    {
        var teamIds = fixtures
            .SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId })
            .Distinct()
            .ToList();

        return await dbContext.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, ct);
    }

    private async Task<Dictionary<int, string?>> LoadSnapshotsAsync(
        IReadOnlyCollection<Fixture> fixtures, string lang, CancellationToken ct)
    {
        var fixtureIds = fixtures.Select(f => f.Id).ToList();

        return await dbContext.FixtureAnalyses
            .AsNoTracking()
            .Where(a => fixtureIds.Contains(a.FixtureId) && a.Lang == lang)
            .ToDictionaryAsync(a => a.FixtureId, a => a.SnapshotJson, ct);
    }

    private async Task<MatchAnalysis?> ResolveSnapshotAsync(
        Fixture fixture, string lang, IReadOnlyDictionary<int, string?> snapshots, CancellationToken ct)
    {
        var snapshot = AnalysisSnapshotSerializer.Deserialize(
            snapshots.GetValueOrDefault(fixture.Id));

        if (snapshot?.DecisionAudit is not null) return snapshot;

        try
        {
            var recomputed = await precomputeService.RecomputeFixtureAsync(fixture.Id, ct);
            return recomputed.GetValueOrDefault(lang);
        }
        catch (Exception ex)
        {
            // One unanalysable fixture must not take the whole board down.
            logger.LogError(ex, "[Picks] Could not analyze fixture {FixtureId}", fixture.Id);
            return null;
        }
    }

    private static FixtureRef ToFixtureRef(
        Fixture fixture, MatchAnalysis snapshot, IReadOnlyDictionary<int, Team> teams)
    {
        var home = teams.GetValueOrDefault(fixture.HomeTeamId);
        var away = teams.GetValueOrDefault(fixture.AwayTeamId);

        return new FixtureRef(
            fixture.Id,
            snapshot.League,
            home?.ShortName ?? home?.Name ?? snapshot.HomeTeam,
            away?.ShortName ?? away?.Name ?? snapshot.AwayTeam,
            fixture.Date);
    }
}
