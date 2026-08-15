using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// Match context - immutable facts about the fixture
/// </summary>
/// <summary>
/// Match context - immutable facts about the fixture
/// </summary>
public sealed class MatchContext
{
    public DateTimeOffset Date { get; init; }
    public TimeSpan Time { get; init; }
    public int LeagueId { get; init; }
    public string LeagueName { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public MatchResult? Result { get; init; }
    
    public double? OddsHome { get; init; }
    public double? OddsDraw { get; init; }
    public double? OddsAway { get; init; }
    public double? OddsOver25 { get; init; }
    public double? OddsUnder25 { get; init; }
    public double? OddsBttsYes { get; init; }
    
    public float? HomeRestDays { get; init; }
    public float? AwayRestDays { get; init; }
}

/// <summary>
/// How a finished fixture turned out, and whether each prediction called it.
///
/// The "Correct" flags compare the prediction to the outcome. The "Actual"
/// fields state the outcome itself. They are separate on purpose: the flags
/// previously carried the raw outcome, so a match where BTTS was predicted
/// "no" and BTTS did not happen was reported as incorrect.
/// </summary>
public sealed class MatchResult
{
    /// <summary>Winner prediction matched the result.</summary>
    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; init; }

    [JsonPropertyName("actual_score")]
    public string ActualScore { get; init; } = string.Empty;

    /// <summary>Null when no prediction was made for the market.</summary>
    [JsonPropertyName("is_btts_correct")]
    public bool? IsBttsCorrect { get; init; }

    [JsonPropertyName("is_over25_correct")]
    public bool? IsOver25Correct { get; init; }

    [JsonPropertyName("is_under25_correct")]
    public bool? IsUnder25Correct { get; init; }

    // ── What actually happened ────────────────────────────────────
    [JsonPropertyName("home_goals")] public int? HomeGoals { get; init; }
    [JsonPropertyName("away_goals")] public int? AwayGoals { get; init; }
    [JsonPropertyName("total_goals")] public int? TotalGoals { get; init; }
    [JsonPropertyName("actual_btts")] public bool? ActualBtts { get; init; }
    [JsonPropertyName("actual_over25")] public bool? ActualOver25 { get; init; }

    /// <summary>home | draw | away — what the model called, for display next to the score.</summary>
    [JsonPropertyName("predicted_winner")] public string? PredictedWinner { get; init; }

    /// <summary>home | draw | away — what actually happened.</summary>
    [JsonPropertyName("actual_winner")] public string? ActualWinner { get; init; }
}

/// <summary>
/// The single call the system stands behind for a fixture, and — once played —
/// whether it landed.
///
/// Every market's probability stays available on <c>prediction</c>; this is the
/// one the model would actually back, so accuracy is one number rather than a
/// per-market grid that can read as three-quarters right on a match the system
/// got wrong.
///
/// The market with the highest probability wins the slot. That is the same rule
/// the confidence picks use, so the headline here and the pick the product sells
/// can never disagree.
/// </summary>
public sealed class HeadlinePrediction
{
    /// <summary>over_2_5 | under_2_5 | btts | no_btts | home_win | draw | away_win</summary>
    [JsonPropertyName("market")] public required string Market { get; init; }

    /// <summary>Human-readable, e.g. "Over 2.5 Goals".</summary>
    [JsonPropertyName("selection")] public required string Selection { get; init; }

    /// <summary>Model probability for this call, 0-1.</summary>
    [JsonPropertyName("probability")] public required double Probability { get; init; }

    /// <summary>Null until the fixture has finished.</summary>
    [JsonPropertyName("is_correct")] public bool? IsCorrect { get; init; }
}

public sealed class TeamStats
{
    // ---------- TEAM INFO ----------
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("rank")]
    public int Rank { get; set; }
    
    [JsonPropertyName("points")]
    public int Points { get; set; }
    
    [JsonIgnore]
    public int Played { get; set; }
    
    [JsonPropertyName("form")]
    public string Form { get; set; } = "";
    
    [JsonPropertyName("form_percentage")]
    public int FormPercentage { get; set; }

    [JsonPropertyName("possession")]
    public double Possession { get; set; }
    
    [JsonPropertyName("momentum")]
    public double Momentum { get; set; }

    [JsonPropertyName("motivation_score")]
    public double MotivationScore { get; set; }
    
    [JsonPropertyName("is_new_manager")]
    public bool IsNewManager { get; set; }
    
    [JsonPropertyName("has_red_card_hangover")]
    public bool HasRedCardHangover { get; set; }

    // ---------- LAST 3 OVERALL ----------
    [JsonPropertyName("avg_goals_scored_last_3")]
    public double AvgGoalsScoredLast3 { get; set; }
    
    [JsonPropertyName("avg_goals_conceded_last_3")]
    public double AvgGoalsConcededLast3 { get; set; }
    
    [JsonPropertyName("btts_rate_last_3")]
    public double BTTSRateLast3 { get; set; }
    
    [JsonPropertyName("over_25_rate_last_3")]
    public double Over25RateLast3 { get; set; }

    // ---------- LAST 7 OVERALL (Mainly internal) ----------
    [JsonPropertyName("avg_goals_scored_last_7")]
    public double AvgGoalsScoredLast7 { get; set; }
    
    [JsonPropertyName("avg_goals_conceded_last_7")]
    public double AvgGoalsConcededLast7 { get; set; }

    [JsonIgnore]
    public double BTTSRateLast7 { get; set; }
    
    [JsonIgnore]
    public double Over25RateLast7 { get; set; }

    // ---------- PERFORMANCE ----------
    [JsonPropertyName("attack_strength")]
    public double AttackStrength { get; set; }
    
    [JsonPropertyName("defensive_strength")]
    public double DefensiveStrength { get; set; }

    // ---------- RESULTS ----------
    [JsonPropertyName("clean_sheet_rate")]
    public double CleanSheetRate { get; set; }
    
    [JsonPropertyName("win_rate")]
    public double WinRate { get; set; }
    
    [JsonPropertyName("zero_zero_matches")]
    public int ZeroZeroMatches { get; set; }

    [JsonIgnore]
    public double ZeroZeroRate { get; set; }
    
    [JsonIgnore]
    public double DrawRate { get; set; }

    public static TeamStats Empty => new();
}

/// <summary>
/// Final weighted stats for both teams
/// </summary>
public sealed class TeamStatsResponse
{
    public TeamStats Home { get; init; } = TeamStats.Empty;
    public TeamStats Away { get; init; } = TeamStats.Empty;
}
