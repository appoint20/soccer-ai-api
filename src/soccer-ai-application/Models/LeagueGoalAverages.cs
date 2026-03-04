namespace SoccerAi.Application.Models;

public sealed class LeagueGoalAverages
{
    public string League { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int MatchesPlayed { get; init; }

    public double HomeGoalsAvg { get; init; }
    public double AwayGoalsAvg { get; init; }
    
    public bool IsValid => MatchesPlayed >= 10 && HomeGoalsAvg > 0 && AwayGoalsAvg > 0;
}
