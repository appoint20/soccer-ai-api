namespace SoccerAi.Application.Models;

public sealed class TeamAggregatedStats
{
    public int MatchesPlayed { get; set; }

    public int GoalsScored { get; set; }
    public int GoalsConceded { get; set; }

    public double GoalsScoredAvg { get; set; }
    public double GoalsConcededAvg { get; set; }

    public double Wins { get; set; }
    public double Draws { get; set; }
    public double Losses { get; set; }

    public double Over25Avg { get; set; }
    public double BothTeamsScoredAvg { get; set; }
    public double TwoToThreeGoalsAvg { get; set; }

    public double CleanSheetAvg { get; set; }
    public double FailedToScoreAvg { get; set; }

    public string Form { get; set; } = string.Empty;
}
