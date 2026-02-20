namespace soccer_gpt_application.Services;

/// <summary>
/// Adjusts BTTS probability for goal scoring correlation.
/// Poisson assumes independence, but real football has momentum:
/// when one team scores, the game opens up → other team more likely to score.
/// </summary>
public static class GoalCorrelation
{
    /// <summary>
    /// Adjust BTTS probability upward based on expected goal correlation.
    /// Higher λ = more likely the game opens up = more correlation.
    /// </summary>
    public static double AdjustBTTS(
        double independentProb,
        double lambdaHome,
        double lambdaAway)
    {
        // Correlation factor: min(λH, λA) × 0.05
        // High-scoring games have more momentum effects
        double correlation = Math.Min(lambdaHome, lambdaAway) * 0.05;

        return Math.Clamp(independentProb + correlation, 0, 1);
    }
}
