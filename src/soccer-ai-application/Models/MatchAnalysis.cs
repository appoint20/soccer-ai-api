using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

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
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public TimeSpan Time { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public MatchResult? Result { get; init; }
    
    public double? OddsHomeWin { get; init; }
    public double? OddsDraw { get; init; }
    public double? OddsAwayWin { get; init; }
    public double? OddsOver25 { get; init; }
    public double? OddsBttsYes { get; init; }
    public double? OddsGoals23 { get; init; } = 1.90;

    
    // Flattened Weighted Stats
    public TeamStats HomeStats { get; init; } = TeamStats.Empty;
    public TeamStats AwayStats { get; init; } = TeamStats.Empty;
    
    // Statistical Models (Poisson + Monte Carlo) — for internal processing only
    [JsonIgnore]
    public StatisticalModels? Models { get; set; }
    
    // Flattened Decisions
    public TrapDecision Trap { get; init; } = new();
    public PredictionResponse? Prediction { get; init; }
    public HeadToHeadModel? H2H { get; init; }
    public AiAnalysisDto? Ai { get; set; }

    /// <summary>Strategic signal catalog — persisted in the snapshot; LLM narratives cite the labels.</summary>
    public Signals.StrategicSignals? Signals { get; init; }

    /// <summary>Which confirm/veto rules fired per market — the backtest and LLM narratives cite this.</summary>
    [JsonPropertyName("decision_audit")]
    public DecisionAudit? DecisionAudit { get; init; }
}
