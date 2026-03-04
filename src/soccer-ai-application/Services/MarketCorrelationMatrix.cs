namespace SoccerAi.Application.Services;

/// <summary>
/// Market correlation matrix — prevents combining strongly correlated legs
/// in parlays (e.g. Over 2.5 + BTTS both depend on goals).
/// Correlation > 0.4 = too correlated for a parlay leg.
/// </summary>
public static class MarketCorrelationMatrix
{
    private static readonly Dictionary<(string, string), double> Correlations = new()
    {
        { ("Over 2.5 Goals", "Both Teams To Score"), 0.75 },
        { ("Over 2.5 Goals", "2-3 Goals"), 0.55 },
        { ("Over 2.5 Goals", "Under 2.5 Goals"), 1.00 },
        { ("Both Teams To Score", "2-3 Goals"), 0.50 },
        { ("Both Teams To Score", "Match Winner"), 0.20 },
        { ("Match Winner", "Under 2.5 Goals"), 0.10 },
        { ("Match Winner", "Over 2.5 Goals"), 0.15 },
        { ("Match Winner", "2-3 Goals"), 0.10 },
        { ("Under 2.5 Goals", "2-3 Goals"), 0.45 },
    };

    /// <summary>
    /// Returns true if two markets are too correlated to combine in a parlay (> 0.4).
    /// </summary>
    public static bool IsTooCorrelated(string a, string b)
    {
        if (a == b) return true;
        if (Correlations.TryGetValue((a, b), out var v)) return v > 0.4;
        if (Correlations.TryGetValue((b, a), out v)) return v > 0.4;
        return false;
    }
}
