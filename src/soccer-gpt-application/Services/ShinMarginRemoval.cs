namespace soccer_gpt_application.Services;

/// <summary>
/// Shin method for removing bookmaker margin.
/// Unlike naive 1/odds normalization, Shin estimates the insider
/// trading parameter (z) and produces true fair probabilities.
/// </summary>
public static class ShinMarginRemoval
{
    /// <summary>
    /// Convert bookmaker decimal odds to true probabilities using Shin's method.
    /// Iteratively solves for the insider parameter z.
    /// </summary>
    public static double[] TrueProbabilities(double[] odds)
    {
        if (odds.Length == 0 || odds.Any(o => o <= 0))
            return odds.Select(_ => 0.0).ToArray();

        var inv = odds.Select(o => 1.0 / o).ToArray();
        int n = inv.Length;

        // Iterative solver for insider parameter z
        double z = 0.05;
        for (int iter = 0; iter < 100; iter++)
        {
            double sum = inv.Sum(p => Math.Sqrt(z * z + 4 * (1 - z) * p * p));
            z = (sum - 2.0) / (n - 2.0);
            z = Math.Clamp(z, 0.001, 0.20);
        }

        // Apply Shin formula
        return inv
            .Select(p =>
                (Math.Sqrt(z * z + 4 * (1 - z) * p * p) - z) /
                (2 * (1 - z)))
            .ToArray();
    }

    /// <summary>
    /// Convenience: convert two-outcome odds (e.g. Over/Under) to true probability.
    /// Returns probability for the first outcome.
    /// </summary>
    public static double TrueProbability(double oddsFor, double oddsAgainst)
    {
        if (oddsFor <= 0 || oddsAgainst <= 0)
            return oddsFor > 0 ? 1.0 / oddsFor : 0;

        var probs = TrueProbabilities([oddsFor, oddsAgainst]);
        return probs[0];
    }
}
