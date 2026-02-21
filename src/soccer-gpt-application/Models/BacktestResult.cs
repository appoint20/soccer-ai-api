namespace soccer_gpt_application.Models;

/// <summary>
/// Complete backtest results with accuracy per market
/// </summary>
public sealed class BacktestResult
{
    public int WeeksAnalyzed { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int TotalMatchesProcessed { get; init; }
    
    public MarketAccuracy BTTS { get; init; } = new();
    public MarketAccuracy DecisionBtts { get; init; } = new(); // Stricter criteria
    public MarketAccuracy DecisionOver25 { get; init; } = new();
    public MarketAccuracy Draw { get; init; } = new();
    public MarketAccuracy Over25 { get; init; } = new();
    public MarketAccuracy HomeWin { get; init; } = new();
    public MarketAccuracy AwayWin { get; init; } = new();
    public MarketAccuracy TwoToThreeGoals { get; init; } = new();
    
    public List<WeeklyBreakdown> WeeklyBreakdown { get; init; } = [];
    
    // Wrong predictions for analysis
    public List<WrongPrediction> WrongBtts { get; init; } = [];
    public List<WrongPrediction> WrongDecisionBtts { get; init; } = [];
    public List<WrongPrediction> WrongDecisionOver25 { get; init; } = [];
}

public sealed class MarketAccuracy
{
    public int QualifiedCount { get; init; }
    public int CorrectPredictions { get; init; }
    public double AccuracyRate => QualifiedCount > 0 ? Math.Round((double)CorrectPredictions / QualifiedCount, 4) : 0;
    public double AccuracyPercent => AccuracyRate * 100;
}

public sealed class WeeklyBreakdown
{
    public int WeekNumber { get; init; }
    public DateTime WeekStart { get; init; }
    public DateTime WeekEnd { get; init; }
    public int MatchesProcessed { get; init; }
    
    public MarketAccuracy BTTS { get; init; } = new();
    public MarketAccuracy Draw { get; init; } = new();
    public MarketAccuracy Over25 { get; init; } = new();
}

public sealed class WrongPrediction
{
    public DateTime Date { get; init; }
    public string League { get; init; } = "";
    public string HomeTeam { get; init; } = "";
    public string AwayTeam { get; init; } = "";
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public double Confidence { get; init; }
    public double HomeGoalsScoredAvg { get; init; }
    public double AwayGoalsScoredAvg { get; init; }
    public double HomeBttsRate { get; init; }
    public double AwayBttsRate { get; init; }
}

