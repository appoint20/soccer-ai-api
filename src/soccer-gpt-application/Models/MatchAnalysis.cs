namespace soccer_gpt_application.Models;

/// <summary>
/// Clean match analysis response following strict separation of concerns:
/// - matchContext: immutable facts
/// - teamSnapshots: numeric rates only
/// - models: pure math (Poisson, Monte Carlo)
/// - headToHead: historical rates
/// - signals: normalized 0-1 indicators
/// - decisions: final qualifications only
/// </summary>
public sealed class MatchAnalysis
{
    public DateTime Date { get; init; }
    public TimeSpan Time { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public MatchResult? Result { get; init; }
    
    public double OddsHomeWin { get; init; }
    public double OddsDraw { get; init; }
    public double OddsAwayWin { get; init; }
    public double OddsOver25 { get; init; }
    public double OddsBttsYes { get; init; }

    
    // Flattened Weighted Stats
    public TeamStats HomeStats { get; init; } = TeamStats.Empty;
    public TeamStats AwayStats { get; init; } = TeamStats.Empty;
    
    // Statistical Models (Poisson + Monte Carlo) — for analysis/debugging
    public StatisticalModels? Models { get; init; }
    
    // Flattened Decisions
    public TrapDecision Trap { get; init; } = new();
    public PredictionResponse? Prediction { get; init; }
    public HeadToHeadModel? H2H { get; init; }
    public GeminiAnalysis? Gemini { get; set; }
}
