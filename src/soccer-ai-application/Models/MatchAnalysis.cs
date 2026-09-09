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
    public string Status { get; set; } = "NS";
    [JsonPropertyName("odds_checked_at_utc")] public DateTimeOffset? OddsCheckedAtUtc { get; set; }
    [JsonPropertyName("odds_updated_at_utc")] public DateTimeOffset? OddsUpdatedAtUtc { get; set; }
    public TimeSpan Time { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public MatchResult? Result { get; init; }

    /// <summary>
    /// The one call the system backs for this fixture, and whether it landed.
    /// Read this for "was the prediction right"; read <c>prediction</c> for the
    /// full per-market probabilities.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("headline_prediction")]
    public HeadlinePrediction? Headline { get; init; }
    
    public double? OddsHomeWin { get; set; }
    public double? OddsDraw { get; set; }
    public double? OddsAwayWin { get; set; }
    public double? OddsOver25 { get; set; }
    public double? OddsUnder25 { get; set; }
    public double? OddsBttsYes { get; set; }
    // odds_goals23 removed: it was a hardcoded 1.90 placeholder, never a real
    // quote. 2-3 goals is informational and never becomes a bet, so a synthetic
    // price on it is exactly the placeholder the product rules forbid. The
    // MinOddsGoals23 strategy threshold is unrelated and still applies.

    /// <summary>
    /// True joint P(BTTS ∧ Over 2.5) from the Dixon-Coles score matrix, needed
    /// to price same-match doubles. Persisted because <see cref="Models"/> is
    /// excluded from the snapshot, and the product of the two market
    /// probabilities is not a valid substitute — they are correlated.
    /// </summary>
    [JsonPropertyName("btts_and_over25_probability")]
    public double? BttsAndOver25Probability { get; init; }

    
    // Flattened Weighted Stats
    public TeamStats HomeStats { get; init; } = TeamStats.Empty;
    public TeamStats AwayStats { get; init; } = TeamStats.Empty;
    
    // Statistical Models (Poisson + Monte Carlo) — for internal processing only
    [JsonIgnore]
    public StatisticalModels? Models { get; set; }
    
    // Flattened Decisions
    public PredictionResponse? Prediction { get; init; }
    public HeadToHeadModel? H2H { get; init; }
    public AiAnalysisDto? Ai { get; set; }

    /// <summary>Strategic signal catalog — persisted in the snapshot; LLM narratives cite the labels.</summary>
    public Signals.StrategicSignals? Signals { get; init; }

    /// <summary>Which confirm/veto rules fired per market — the backtest and LLM narratives cite this.</summary>
    [JsonPropertyName("decision_audit")]
    public DecisionAudit? DecisionAudit { get; set; }

    /// <summary>Raw vs isotonic-calibrated probability per market.</summary>
    [JsonPropertyName("calibration_trace")]
    public IReadOnlyList<Interfaces.CalibrationTraceEntry>? CalibrationTrace { get; init; }
}
