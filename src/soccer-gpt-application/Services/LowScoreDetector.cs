namespace soccer_gpt_application.Services;

/// <summary>
/// Pure Poisson math for detecting low-scoring match profiles.
/// P(0-0) = e^(-λ_home) × e^(-λ_away)
/// </summary>
public static class LowScoreDetector
{
    /// <summary>Probability of a 0-0 draw from expected goals.</summary>
    public static double Probability00(double lambdaHome, double lambdaAway)
        => Math.Exp(-lambdaHome) * Math.Exp(-lambdaAway);

    /// <summary>
    /// Match is a low-scoring trap when P(0-0) exceeds threshold.
    /// Threshold 0.18 ≈ both teams expected to score &lt; 0.9 goals each.
    /// </summary>
    public static bool IsLowScoringTrap(double lambdaHome, double lambdaAway)
        => Probability00(lambdaHome, lambdaAway) > 0.18;

    /// <summary>Probability of Under 1.5 goals (0-0, 1-0, 0-1).</summary>
    public static double ProbabilityUnder15(double lambdaHome, double lambdaAway)
    {
        var p00 = Probability00(lambdaHome, lambdaAway);
        var p10 = lambdaHome * Math.Exp(-lambdaHome) * Math.Exp(-lambdaAway);
        var p01 = Math.Exp(-lambdaHome) * lambdaAway * Math.Exp(-lambdaAway);
        return p00 + p10 + p01;
    }
}
