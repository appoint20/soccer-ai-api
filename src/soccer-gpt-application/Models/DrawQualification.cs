namespace soccer_gpt_application.Models;

/// <summary>
/// Draw qualification signal with component scores
/// </summary>
public sealed class DrawQualification
{
    // Final qualification score (0 to 1)
    public double Score { get; init; }
    
    // Qualification status based on thresholds
    public bool IsQualified => Score >= 0.55;
    public bool IsStrongQualified => Score >= 0.70;
    
    // Component signals (0 to 1 each)
    public double PoissonDrawSignal { get; init; }
    public double GoalBalanceSignal { get; init; }
    public double ScoringDrawsSignal { get; init; }  // 1-1, 2-2 probability mass
    public double H2HDrawSignal { get; init; }
    
    // Raw values for explainability
    public double LambdaDifference { get; init; }
    public double HighScoringDrawProb { get; init; }  // P(1-1) + P(2-2)
    
    public string Status => Score switch
    {
        >= 0.70 => "Strong Draw Candidate",
        >= 0.55 => "Draw Candidate", 
        >= 0.40 => "Possible Draw",
        _ => "Low Draw Likelihood"
    };
    
    public static DrawQualification Empty => new()
    {
        Score = 0,
        PoissonDrawSignal = 0,
        GoalBalanceSignal = 0,
        ScoringDrawsSignal = 0,
        H2HDrawSignal = 0
    };
}
