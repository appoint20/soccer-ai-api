using SoccerAi.Application.Services;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>Guard-sanitized market prices for one fixture (null = no valid odds).</summary>
public sealed record MarketPrices(
    double? HomeWin,
    double? Draw,
    double? AwayWin,
    double? Over25,
    double? Under25,
    double? BttsYes,
    double? Goals23)
{
    public static MarketPrices FromRaw(
        double? homeWin, double? draw, double? awayWin,
        double? over25, double? under25, double? bttsYes, double? goals23 = null) =>
        new(OddsGuard.Sanitize(homeWin), OddsGuard.Sanitize(draw), OddsGuard.Sanitize(awayWin),
            OddsGuard.Sanitize(over25), OddsGuard.Sanitize(under25),
            OddsGuard.Sanitize(bttsYes), OddsGuard.Sanitize(goals23));

    public static MarketPrices Empty { get; } = new(null, null, null, null, null, null, null);
}

/// <summary>Outcome of the value-gate chain, in evaluation order.</summary>
public static class GateOutcome
{
    /// <summary>Market flagged informational-only (no odds exist at source, ever).</summary>
    public const string InformationalOnly = "informational_only";

    public const string AnalysisOnlyNoOdds = "analysis_only_no_odds";
    public const string BelowMinOdds = "below_min_odds";
    public const string BelowMinEdge = "below_min_edge";
    public const string BelowProbabilityFloor = "below_probability_floor";
    public const string Vetoed = "vetoed";
    public const string InsufficientConfirms = "insufficient_confirms";
    public const string Qualified = "qualified";
}

/// <summary>
/// Pure EV / Kelly math for the value gate.
/// EV = p × odds − 1. Kelly f* = (p(b+1) − 1) / b with b = odds − 1.
/// </summary>
public static class ValueMath
{
    public static double Ev(double probability, double odds) => probability * odds - 1;

    /// <summary>Fractional Kelly stake (share of bankroll), 0 when no edge.</summary>
    public static double FractionalKelly(double probability, double odds, double fraction) =>
        Math.Round(KellyCriterion.Fraction(probability, odds) * fraction, 4);
}
