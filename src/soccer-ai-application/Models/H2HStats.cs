namespace SoccerAi.Application.Models;

public sealed class H2HStats
{
    public int MatchesAnalyzed { get; init; }
    public int HomeWins { get; init; }
    public int AwayWins { get; init; }
    public int Draws { get; init; }
    
    public double AvgGoalsHome { get; init; }
    public double AvgGoalsAway { get; init; }
    
    public double BothTeamScoredGoal { get; init; }
    public double Over25Rate { get; init; }
    public double TwoToThreeGoalsRate { get; init; }
    
    public string HomeForm { get; init; } = string.Empty;
    public string AwayForm { get; init; } = string.Empty;
    
    public string Status { get; init; } = "Insufficient Data";
    
    public bool IsValid => MatchesAnalyzed >= 3;
    
    public static H2HStats Insufficient => new()
    {
        MatchesAnalyzed = 0,
        Status = "Insufficient Data"
    };
}
