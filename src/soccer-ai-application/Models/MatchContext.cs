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
/// <summary>
/// Whether one market's call was right.
/// </summary>
/// <remarks>
/// Present only for a market that genuinely has a verdict. An absent entry
/// means "not judged", which is a different fact from <c>correct: false</c> and
/// must never be collapsed into it — a pick nobody can score is not a pick that
/// lost.
/// </remarks>
/// <param name="Market">
/// Market key, spelled as in <c>decision_audit.markets[]</c>: <c>btts</c>,
/// <c>over25</c>, <c>under25</c>, <c>goals_2_3</c>, <c>match_winner</c> or
/// <c>draw</c>.
/// </param>
/// <param name="Correct">
/// Whether the call matched the outcome — including a correct call that the
/// market would not hit.
/// </param>
public sealed record MarketVerdict(
    [property: JsonPropertyName("market")] string Market,
    [property: JsonPropertyName("correct")] bool Correct);

/// <summary>
/// Whether a fixture's outcome can be counted.
/// </summary>
/// <remarks>
/// Only <c>settled</c> may enter a hit-rate or ROI figure. The rest finished in
/// a way that never produced a market outcome, and counting them as losses
/// would understate the record — a postponed match is not a failed prediction.
/// </remarks>
public static class ResultStatus
{
    /// <summary>Played to a conclusion — verdicts are meaningful.</summary>
    public const string Settled = "settled";

    /// <summary>Awarded or walked over: a result exists on paper, but no market played out.</summary>
    public const string Void = "void";

    /// <summary>Called off before kick-off.</summary>
    public const string Postponed = "postponed";

    /// <summary>Started but never finished.</summary>
    public const string Abandoned = "abandoned";
}

public sealed class MatchResult
{
    /// <summary>
    /// <c>settled</c> | <c>void</c> | <c>postponed</c> | <c>abandoned</c>.
    /// Anything other than <c>settled</c> must be excluded from both the correct
    /// and the wrong counts.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = ResultStatus.Settled;

    /// <summary>
    /// Per-market verdicts, keyed as in <c>decision_audit.markets[]</c>, so a
    /// pick is scored on the market it was actually made in rather than on the
    /// 1X2 outcome. Empty when nothing could be judged.
    /// </summary>
    [JsonPropertyName("markets")]
    public IReadOnlyList<MarketVerdict> Markets { get; init; } = [];

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
    
    /// <summary>Points won over the last 5 results as a share of 15. Range 0-100.</summary>
    [JsonPropertyName("form_percentage")]
    public int FormPercentage { get; set; }

    [JsonPropertyName("possession")]
    public double Possession { get; set; }
    
    /// <summary>
    /// Weighted recent form: 70 points from the last 3 matches, 30 from the
    /// previous 4, plus a win-streak bonus. Range 0-100 — the sum is capped, so
    /// the streak bonus cannot carry it past 100. Effectively a percentage.
    /// </summary>
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
    
    /// <summary>Share of the last 3 matches with both teams scoring. Range 0-1.</summary>
    [JsonPropertyName("btts_rate_last_3")]
    public double BTTSRateLast3 { get; set; }
    
    /// <summary>Share of the last 3 matches going over 2.5 goals. Range 0-1.</summary>
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
    /// <summary>
    /// Goals scored per match, weighted 60% at this venue and 40% overall.
    /// An absolute rate in goals, NOT a ratio — there is no neutral 1.0. Do not
    /// confuse it with TeamStrength.HomeAttackStrength, which IS a ratio against
    /// the league average and where 1.0 does mean average.
    /// </summary>
    [JsonPropertyName("attack_strength")]
    public double AttackStrength { get; set; }
    
    /// <summary>
    /// Goals conceded per match, weighted 60% at this venue and 40% overall.
    /// An absolute rate in goals, NOT a ratio — lower is better, and there is no
    /// neutral 1.0.
    /// </summary>
    [JsonPropertyName("defensive_strength")]
    public double DefensiveStrength { get; set; }

    // ---------- RESULTS ----------
    /// <summary>Share of the last 7 matches without conceding. Range 0-1.</summary>
    [JsonPropertyName("clean_sheet_rate")]
    public double CleanSheetRate { get; set; }
    
    /// <summary>Share of the last 7 matches won. Range 0-1.</summary>
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
