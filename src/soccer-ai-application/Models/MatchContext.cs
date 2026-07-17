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

public sealed class MatchResult
{
    public bool IsCorrect { get; init; }
    public string ActualScore { get; init; } = string.Empty;
    public bool? IsBttsCorrect { get; init; }
    public bool? IsOver25Correct { get; init; }
    public bool? IsUnder25Correct { get; init; }
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
