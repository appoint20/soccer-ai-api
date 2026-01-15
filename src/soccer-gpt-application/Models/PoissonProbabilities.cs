namespace soccer_gpt_application.Models;

public sealed class PoissonProbabilities
{
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    
    public double Over25 { get; init; }
    public double Under25 { get; init; }
    public double BTTS { get; init; }
    public double BTTSNo { get; init; }
    
    public double TwoToThreeGoals { get; init; }
    public double HomeExpectedGoals { get; init; }
    public double AwayExpectedGoals { get; init; }
    
    public string MostLikelyScore { get; init; } = string.Empty;
    public double MostLikelyScoreProbability { get; init; }
    
    // Top 5 most likely scores
    public List<ScoreProbability> TopScores { get; init; } = [];
}

public sealed class ScoreProbability
{
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public double Probability { get; init; }
    
    public string Score => $"{HomeGoals}:{AwayGoals}";
}
