namespace soccer_gpt_application.Models;

/// <summary>
/// Recent form statistics for a team
/// </summary>
public class FormStats
{
    public double CleanSheetRate { get; set; }
    public double FailedToScoreRate { get; set; }
    public int MatchesAnalyzed { get; set; }
    
    public static FormStats Default()
    {
        return new FormStats
        {
            CleanSheetRate = 0,
            FailedToScoreRate = 0,
            MatchesAnalyzed = 0
        };
    }
}
