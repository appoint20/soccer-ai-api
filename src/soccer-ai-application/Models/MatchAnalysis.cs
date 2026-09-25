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

    /// <summary>
    /// Minutes played, while the match is in play; null otherwise.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="Status"/>, which names the period: 45 in "HT"
    /// means the half ended, 45 in "1H" means it is still running.
    /// </remarks>
    [JsonPropertyName("elapsed_minutes")] public int? ElapsedMinutes { get; set; }

    /// <summary>Added time in the current period, when the provider reports it.</summary>
    [JsonPropertyName("extra_minutes")] public int? ExtraMinutes { get; set; }

    /// <summary>The score as it stands. Zero-zero before kickoff.</summary>
    [JsonPropertyName("live_home_goals")] public int? LiveHomeGoals { get; set; }

    [JsonPropertyName("live_away_goals")] public int? LiveAwayGoals { get; set; }
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
    public double? OddsGoals23 { get; set; }
    public double? OddsBttsAndOver25 { get; set; }
    public string? OddsBookmaker { get; set; }

    /// <summary>
    /// Whether the prices above are current enough to bet on, by
    /// <see cref="Services.LiveOddsPolicy.IsFresh"/>.
    /// </summary>
    /// <remarks>
    /// False does not mean the prices are wrong — they are the last real market
    /// prices — only that the provider has not refreshed them recently, which is
    /// normal for a fixture days away. Show them with their age; no tip is
    /// qualified from them.
    /// </remarks>
    [JsonPropertyName("odds_are_live")] public bool OddsAreLive { get; set; }

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
    /// <remarks>
    /// Named explicitly because the global SnakeCaseLower policy renders `H2H`
    /// as "h2_h" — an unguessable key that no client was reading, which is why
    /// the head-to-head section never appeared in the app. Clients accept both
    /// spellings, so this can be corrected without a lockstep release.
    /// </remarks>
    [JsonPropertyName("h2h")]
    public HeadToHeadModel? H2H { get; init; }

    /// <summary>
    /// The provider's own read of this fixture — outcome percentages and a
    /// side-by-side comparison. Null when it publishes none for the division.
    /// </summary>
    [JsonPropertyName("provider_prediction")]
    public ProviderPrediction? Provider { get; init; }
    public AiAnalysisDto? Ai { get; set; }
    public AiDecisionExplanation? DecisionExplanation { get; set; }
    public string PresentationLanguage { get; set; } = "en";
    public DecisionPresentation? Presentation { get; set; }

    /// <summary>Strategic signal catalog — persisted in the snapshot; LLM narratives cite the labels.</summary>
    public Signals.StrategicSignals? Signals { get; init; }

    /// <summary>Which confirm/veto rules fired per market — the backtest and LLM narratives cite this.</summary>
    [JsonPropertyName("decision_audit")]
    public DecisionAudit? DecisionAudit { get; set; }

    /// <summary>Raw vs isotonic-calibrated probability per market.</summary>
    [JsonPropertyName("calibration_trace")]
    public IReadOnlyList<Interfaces.CalibrationTraceEntry>? CalibrationTrace { get; init; }
}
