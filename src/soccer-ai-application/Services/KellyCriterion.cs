namespace SoccerAi.Application.Services;

/// <summary>
/// Kelly Criterion bet sizing — professional bankroll management.
/// Filters out bets where the edge is too thin to be profitable.
/// </summary>
public static class KellyCriterion
{
    /// <summary>
    /// Calculate optimal bet fraction using Kelly formula.
    /// f* = (p(b+1) - 1) / b  where p=probability, b=odds-1
    /// Returns 0 if edge is negative.
    /// </summary>
    public static double Fraction(double probability, double odds)
    {
        if (odds <= 1 || probability <= 0 || probability >= 1)
            return 0;

        double b = odds - 1;
        double kelly = (probability * (b + 1) - 1) / b;
        return Math.Max(kelly, 0);
    }

    /// <summary>
    /// Returns true if the Kelly fraction meets the minimum threshold (default 3%).
    /// Bets below this threshold have too thin an edge.
    /// </summary>
    public static bool IsWorthBetting(double probability, double odds, double minFraction = 0.03)
        => Fraction(probability, odds) >= minFraction;
}
