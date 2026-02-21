namespace soccer_gpt_application.Models;

public sealed class PoissonProbabilities
{
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    
    public double Over25 { get; init; }
    public double BothTeamScoredGoal { get; init; }
    
    public double TwoToThreeGoals { get; init; }
    public double HomeExpectedGoals { get; init; }
    public double AwayExpectedGoals { get; init; }

    public double Score00 { get; init; }
    public double Score10 { get; init; }
    public double Score01 { get; init; }
    public double Score11 { get; init; }
}
