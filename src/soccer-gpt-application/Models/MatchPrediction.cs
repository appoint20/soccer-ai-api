namespace soccer_gpt_application.Models;

/// <summary>
/// Complete match prediction with all necessary data for defensive filtering
/// </summary>
public class MatchPrediction
{
    public string League { get; set; } = "";
    public double HomeXG { get; set; }
    public double AwayXG { get; set; }
    public double Over25Probability { get; set; }
    public double BTTSProbability { get; set; }
    public double HomeWinProbability { get; set; }
    public double DrawProbability { get; set; }
    public double AwayWinProbability { get; set; }

    public TeamStats Home { get; set; } = new();
    public TeamStats Away { get; set; } = new();
    public ContextFlags Context { get; set; } = new();
    
    // Calculated properties
    public double XGDiff => Math.Abs(HomeXG - AwayXG);
    public double FavoriteWinProbability => Math.Max(HomeWinProbability, AwayWinProbability);
    public double UnderdogAvgGoalsForLast10 => HomeWinProbability > AwayWinProbability 
        ? Away.AvgGoalsForLast10 
        : Home.AvgGoalsForLast10;
}
