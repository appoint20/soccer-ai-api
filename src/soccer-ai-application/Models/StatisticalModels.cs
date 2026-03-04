using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// Poisson model output - pure math, no decisions, no topScores
/// </summary>
public sealed class PoissonModel
{
    // Expected goals (lambdas) - Hidden from JSON but used for signals
    [JsonIgnore]
    public double ExpectedHomeGoals { get; init; }
    [JsonIgnore]
    public double ExpectedAwayGoals { get; init; }
    [JsonIgnore]
    public double ExpectedScoreDifference { get; init; }
    
    [JsonIgnore] 
    public double Score00 { get; init; }
    [JsonIgnore] 
    public double Score10 { get; init; }
    [JsonIgnore] 
    public double Score01 { get; init; }
    [JsonIgnore] 
    public double Score11 { get; init; }
    
    // Outcome probabilities (standard Poisson)
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    
    // Market probabilities
    public double BTTS { get; init; }
    public double Over25 { get; init; }
    public double TwoToThreeGoals { get; init; }
    
    // High-scoring draw profile (1-1 + 2-2)
    public double ScoringDrawProb { get; init; }
    
    public bool IsValid { get; init; }
    
    public static PoissonModel Empty => new();
}

/// <summary>
/// Monte Carlo simulation output - pure math, no decisions, no topScores
/// </summary>
public sealed class MonteCarloModel
{
    public int SimulationCount { get; init; }
    
    // Outcome probabilities
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    
    // Market probabilities
    public double BTTS { get; init; }
    public double Over25 { get; init; }
    public double TwoToThreeGoals { get; init; }
    
    public bool IsValid => SimulationCount > 0;
    
    public static MonteCarloModel Empty => new();
}

/// <summary>
/// Statistical models container
/// </summary>
public sealed class StatisticalModels
{
    public PoissonModel Poisson { get; init; } = PoissonModel.Empty;
    public MonteCarloModel MonteCarlo { get; init; } = MonteCarloModel.Empty;
}
