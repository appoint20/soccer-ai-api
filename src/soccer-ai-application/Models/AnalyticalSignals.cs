namespace SoccerAi.Application.Models;

/// <summary>
/// Analytical signals - all normalized 0-1, AI-friendly
/// These are DERIVED indicators, not decisions
/// </summary>
public sealed class AnalyticalSignals
{
    // Draw-related signals (all 0-1 normalized)
    public double PoissonDrawSignal { get; init; }
    public double MonteCarloDrawSignal { get; init; }
    public double GoalBalanceSignal { get; init; }
    public double ScoringDrawProfileSignal { get; init; }
    public double H2HDrawSignal { get; init; }
    
    // Raw values for explainability
    public double LambdaDifference { get; init; }
    public double HighScoringDrawProbability { get; init; }
    
    // BTTS signals (0-1 normalized)
    public double PoissonBTTSSignal { get; init; }
    public double MonteCarloBTTSSignal { get; init; }
    public double H2HBTTSSignal { get; init; }
    
    // Over 2.5 signals (0-1 normalized)
    public double PoissonOver25Signal { get; init; }
    public double MonteCarloOver25Signal { get; init; }
    public double H2HOver25Signal { get; init; }
    
    // Combined weighted signals
    public double DrawCombinedSignal { get; init; }
    public double BTTSCombinedSignal { get; init; }
    public double Over25CombinedSignal { get; init; }
    
    public static AnalyticalSignals Empty => new();
}
