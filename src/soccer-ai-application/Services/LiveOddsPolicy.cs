using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Services;

/// <summary>Revalidates cached analyses against today's obtainable pre-match prices.</summary>
public static class LiveOddsPolicy
{
    public const double MinimumOdds = 1.70;
    public const string Bookmaker = "Bet365";
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(3);

    public static bool IsFresh(Fixture fixture, DateTimeOffset now) =>
        fixture.Status == "NS" && fixture.Date > now &&
        string.Equals(fixture.OddsBookmaker, Bookmaker, StringComparison.OrdinalIgnoreCase) &&
        fixture.OddsCheckedAtUtc is { } captured && captured <= now && now - captured <= MaximumAge &&
        fixture.OddsUpdatedAtUtc is { } updated && updated <= captured && now - updated <= MaximumAge;

    public static double? PriceFor(Fixture fixture, MarketRuleAudit market) => market.Market switch
    {
        ConfluenceRuleEngine.Markets.Btts => fixture.BttsYesOdds,
        ConfluenceRuleEngine.Markets.Goals23 => fixture.Goals23Odds,
        ConfluenceRuleEngine.Markets.BttsAndOver25 => fixture.BttsAndOver25Odds,
        ConfluenceRuleEngine.Markets.Over25 => fixture.Over25Odds,
        ConfluenceRuleEngine.Markets.Under25 => fixture.Under25Odds,
        ConfluenceRuleEngine.Markets.Draw => fixture.DrawOdds,
        ConfluenceRuleEngine.Markets.MatchWinner when market.Selection == ConfluenceRuleEngine.Selections.MatchWinnerHome => fixture.HomeWinOdds,
        ConfluenceRuleEngine.Markets.MatchWinner when market.Selection == ConfluenceRuleEngine.Selections.MatchWinnerAway => fixture.AwayWinOdds,
        _ => null
    };

    public static DecisionAudit Reprice(DecisionAudit audit, Fixture fixture, DateTimeOffset now)
    {
        var fresh = IsFresh(fixture, now);
        return audit with { Markets = audit.Markets.Select(m =>
        {
            var price = fresh ? OddsGuard.Sanitize(PriceFor(fixture, m)) : null;
            var ev = price is { } odds ? ValueMath.Ev(m.Probability, odds) : (double?)null;
            var floor = Math.Max(MinimumOdds, m.MinOdds);
            var pricePassed = price >= floor;
            var edgePassed = ev > 0 && ev >= m.MinEdge;
            // Repricing may withdraw an old pick, never create a new confluence claim.
            var qualified = m.Qualified && pricePassed && edgePassed;
            return m with
            {
                Odds = price, MinOdds = floor, Ev = ev, Qualified = qualified,
                ComboEligible = m.ComboEligible && pricePassed && edgePassed && m.ProbabilityPassed,
                KellyStake = qualified && price is { } currentOdds && m.KellyFraction is { } fraction
                    ? ValueMath.FractionalKelly(m.Probability, currentOdds, fraction) : null,
                GateOutcome = !fresh ? "stale_odds" : price is null ? GateOutcome.AnalysisOnlyNoOdds
                    : !pricePassed ? GateOutcome.BelowMinOdds : !edgePassed ? GateOutcome.BelowMinEdge : m.GateOutcome
            };
        }).ToList() };
    }

    public static void RefreshResponse(MatchAnalysis snapshot, Fixture fixture, DateTimeOffset now)
    {
        snapshot.OddsCheckedAtUtc = fixture.OddsCheckedAtUtc;
        snapshot.OddsUpdatedAtUtc = fixture.OddsUpdatedAtUtc;
        snapshot.Status = fixture.Status;
        snapshot.OddsBookmaker = fixture.OddsBookmaker;
        if (snapshot.Result is not null)
        {
            Analysis.DecisionExplanationPolicy.Refresh(snapshot);
            return; // historical outcomes keep their recorded analysis
        }
        var fresh = IsFresh(fixture, now);
        // Shown whatever their age, together with OddsUpdatedAtUtc and the flag
        // below, so a reader can see the last real market price and how old it
        // is. Acting on one is a separate question, answered by Reprice: a
        // stale price qualifies nothing, and the gate below is untouched.
        snapshot.OddsAreLive = fresh;
        snapshot.OddsHomeWin = OddsGuard.Sanitize(fixture.HomeWinOdds);
        snapshot.OddsDraw = OddsGuard.Sanitize(fixture.DrawOdds);
        snapshot.OddsAwayWin = OddsGuard.Sanitize(fixture.AwayWinOdds);
        snapshot.OddsOver25 = OddsGuard.Sanitize(fixture.Over25Odds);
        snapshot.OddsUnder25 = OddsGuard.Sanitize(fixture.Under25Odds);
        snapshot.OddsBttsYes = OddsGuard.Sanitize(fixture.BttsYesOdds);
        snapshot.OddsGoals23 = OddsGuard.Sanitize(fixture.Goals23Odds);
        snapshot.OddsBttsAndOver25 = OddsGuard.Sanitize(fixture.BttsAndOver25Odds);
        if (snapshot.DecisionAudit is not { } audit) return;
        snapshot.DecisionAudit = Reprice(audit, fixture, now);
        Analysis.DecisionExplanationPolicy.Refresh(snapshot);
        bool Qualified(string market) => snapshot.DecisionAudit.Markets.Any(m => m.Market == market && m.Qualified);
        if (snapshot.Prediction is not { } p) return;
        p.BTTS.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.Btts);
        p.Over25.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.Over25);
        p.LowScoring.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.Under25);
        p.MatchWinner.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.MatchWinner);
        p.HomeWin.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.MatchWinner);
        p.AwayWin.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.MatchWinner);
        p.Draw.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.Draw);
        p.TwoToThreeGoals.IsQualified &= Qualified(ConfluenceRuleEngine.Markets.Goals23);
    }
}
