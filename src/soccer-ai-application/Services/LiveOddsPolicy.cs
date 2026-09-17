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

    /// <summary>
    /// Attaches today's price to an audited market, and nothing more.
    /// </summary>
    /// <remarks>
    /// This used to withdraw a pick whose price had gone stale, missing or
    /// below the floor. A price is no longer part of the decision — it arrives
    /// late and is absent for most fixtures more than a day out — so a call
    /// made from model probability and evidence stands whatever the market
    /// does. EV and the Kelly stake are still computed when a price exists,
    /// as sizing information for a decision already taken.
    /// </remarks>
    public static DecisionAudit Reprice(DecisionAudit audit, Fixture fixture, DateTimeOffset now)
    {
        return audit with { Markets = audit.Markets.Select(m =>
        {
            var price = OddsGuard.Sanitize(PriceFor(fixture, m));
            var ev = price is { } odds ? ValueMath.Ev(m.Probability, odds) : (double?)null;
            // Only a verdict the retired price gate handed down is recomputed.
            // Everything else is left exactly as the engine decided it: a
            // reprice may never promote a market the confluence rules rejected.
            var retired = RetiredPriceOutcome(m, audit.MinConfirmationsRequired);
            var outcome = retired ?? m.GateOutcome;
            var qualified = retired is null ? m.Qualified : retired == GateOutcome.Qualified;
            return m with
            {
                Odds = price, Ev = ev, GateOutcome = outcome, Qualified = qualified,
                ComboEligible = retired is null ? m.ComboEligible : qualified,
                KellyStake = qualified && price is { } currentOdds && m.KellyFraction is { } fraction
                    ? ValueMath.FractionalKelly(m.Probability, currentOdds, fraction) : null
            };
        }).ToList() };
    }

    /// <summary>
    /// The verdict a stored market would get today, when the one it carries was
    /// handed down by the retired price gate; null when it was not.
    /// </summary>
    /// <remarks>
    /// Snapshots persist their audit, so a market rejected weeks ago for its
    /// price still says so on every read. Re-running the engine costs a model
    /// call per fixture, so the outcome is recomputed here from the flags the
    /// snapshot already carries: the probability floor, vetoes and the
    /// confirmation count are all recorded on the market itself.
    /// </remarks>
    private static string? RetiredPriceOutcome(MarketRuleAudit m, int minConfirmations) =>
        m.GateOutcome is "stale_odds" or GateOutcome.AnalysisOnlyNoOdds
            or GateOutcome.BelowMinOdds or GateOutcome.BelowMinEdge
            ? !m.ProbabilityPassed ? GateOutcome.BelowProbabilityFloor
              : m.VetoesFired > 0 ? GateOutcome.Vetoed
              : m.ConfirmationsFired < minConfirmations ? GateOutcome.InsufficientConfirms
              : m.AiAgrees is false && m.AiAgreementMode == "veto" ? GateOutcome.AiDisagrees
              : GateOutcome.Qualified
            : null;

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
