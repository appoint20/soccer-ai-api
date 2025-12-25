namespace soccer_gpt_application.Models;

/// <summary>
/// Contains all context needed for defensive failure filters
/// </summary>
public class MatchContext
{
    public string League { get; set; } = "";
    public double HomeXG { get; set; }
    public double AwayXG { get; set; }
    public double Over25Probability { get; set; }
    public double BTTSProbability { get; set; }
    public double HomeOdds { get; set; }
    public double AwayOdds { get; set; }
    
    // Recent form (last 10 matches)
    public double HomeCleanSheetRateLast10 { get; set; }
    public double AwayCleanSheetRateLast10 { get; set; }
    public double HomeFailedToScoreRateLast10 { get; set; }
    public double AwayFailedToScoreRateLast10 { get; set; }
    
    // Calculated properties
    public double XGDiff => Math.Abs(HomeXG - AwayXG);
    public double FavoriteOdds => Math.Min(HomeOdds, AwayOdds);
}
