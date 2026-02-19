namespace soccer_gpt_application.Models;

/// <summary>
/// Head-to-head analysis - facts and rates only, no decisions
/// </summary>
public sealed class HeadToHeadModel
{
    public int MatchesAnalyzed { get; init; }
    
    // Rates (normalized 0-1)
    public double DrawRate { get; init; }
    public double HomeWinRate { get; init; }
    public double AwayWinRate { get; init; }
    public double BTTSRate { get; init; }
    public double Over25Rate { get; init; }
    public double TwoToThreeGoalsRate { get; init; }
    
    // Averages
    public double AvgGoalsHome { get; init; }
    public double AvgGoalsAway { get; init; }
    public double AvgTotalGoals { get; init; }
    
    public DateTime? LastMatchDate { get; init; }
    
    public bool IsValid => MatchesAnalyzed >= 3;
    
    public static HeadToHeadModel Empty => new() { MatchesAnalyzed = 0 };
}
