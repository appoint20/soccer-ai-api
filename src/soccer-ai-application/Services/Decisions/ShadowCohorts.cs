using SoccerAi.Application.Models;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>
/// Shadow cohorts: picks the value gate rejected, tracked with full outcomes
/// so the backtest can prove (or kill) each filter. Shadow picks NEVER become
/// real picks — measurement only.
/// </summary>
public static class ShadowCohorts
{
    /// <summary>Rejected ONLY by the MinOdds floor (all later gates would pass).</summary>
    public const string RejectedByMinOdds = "rejected_by_min_odds";

    /// <summary>Rejected ONLY by the MinEdge EV gate (floor/vetoes/confirms pass).</summary>
    public const string RejectedByMinEv = "rejected_by_min_ev";

    /// <summary>Named hypothesis: favorites p≥62% at odds 1.40-2.10.</summary>
    public const string WinnerBand = "winner_p62_odds_140_210";

    /// <summary>Floor + vetoes + confirms — everything after the price gates.</summary>
    public static bool PassesDownstreamGates(MarketRuleAudit a, int minConfirms) =>
        a.ProbabilityPassed && a.VetoesFired == 0 && a.ConfirmationsFired >= minConfirms;

    /// <summary>
    /// Cohorts this market audit belongs to. Honest attribution: a pick only
    /// counts as "rejected by X" when X was the SOLE blocker.
    /// </summary>
    public static List<string> Classify(MarketRuleAudit a, int minConfirms)
    {
        var cohorts = new List<string>();

        if (a.GateOutcome == GateOutcome.BelowMinOdds &&
            a.Ev is not null && a.Ev >= a.MinEdge &&
            PassesDownstreamGates(a, minConfirms))
        {
            cohorts.Add(RejectedByMinOdds);
        }

        if (a.GateOutcome == GateOutcome.BelowMinEdge &&
            PassesDownstreamGates(a, minConfirms))
        {
            cohorts.Add(RejectedByMinEv);
        }

        return cohorts;
    }

    /// <summary>The named winner-band hypothesis (independent of confluence).</summary>
    public static bool InWinnerBand(double winnerProbability, double? odds, ConfluenceOptions opt) =>
        odds is not null &&
        winnerProbability >= opt.ShadowWinnerMinProbability &&
        odds >= opt.ShadowWinnerOddsMin &&
        odds < opt.ShadowWinnerOddsMax;
}
