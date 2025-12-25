namespace soccer_gpt_application.Models;

/// <summary>
/// Team statistics for defensive filter evaluation
/// </summary>
public class TeamStats
{
    public double AvgGoalsForLast10 { get; set; }
    public double AvgGoalsAgainstLast10 { get; set; }
    public double CleanSheetRateLast10 { get; set; }
    public double FailedToScoreRateLast10 { get; set; }
    public double DrawRate { get; set; }
    public double GoalVarianceLast10 { get; set; }
}
