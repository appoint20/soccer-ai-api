namespace SoccerAi.Application.Models;

public sealed class PoissonProbabilities
{
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    
    public double Over25 { get; init; }
    public double BothTeamScoredGoal { get; init; }
    
    public double TwoToThreeGoals { get; init; }

    /// <summary>P(BTTS AND Over 2.5) read off the same score matrix (correlated pair).</summary>
    public double BttsAndOver25 { get; init; }
    public double HomeExpectedGoals { get; init; }
    public double AwayExpectedGoals { get; init; }

}
