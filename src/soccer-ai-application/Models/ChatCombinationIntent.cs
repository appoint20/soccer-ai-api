using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// Represents the structured intent extracted from a natural language betting query.
/// </summary>
public sealed class ChatCombinationIntent
{
    [JsonPropertyName("min_matches")]
    public int MinMatches { get; set; } = 2;

    [JsonPropertyName("max_matches")]
    public int MaxMatches { get; set; } = 3;

    [JsonPropertyName("preferred_markets")]
    public List<string> PreferredMarkets { get; set; } = new(); // e.g., "HomeWin", "AwayWin", "Draw", "BTTS", "Over25"

    [JsonPropertyName("min_total_odds")]
    public double MinTotalOdds { get; set; } = 1.0;

    [JsonPropertyName("min_selection_odds")]
    public double MinSelectionOdds { get; set; } = 1.0;

    [JsonPropertyName("max_same_league")]
    public int MaxSameLeague { get; set; } = 1; // 1 means all must be different leagues

    [JsonPropertyName("market_groups")]
    public List<MarketIntentGroup> MarketGroups { get; set; } = new();

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "balanced"; // safe, balanced, aggressive

    [JsonPropertyName("preferred_leagues")]
    public List<string> PreferredLeagues { get; set; } = new();

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceType { get; set; } = "SYSTEM"; // Changed from init to set so handlers can easily override

    [JsonPropertyName("time_frame")]
    public TimeConstraint? TimeFrame { get; set; }
}

public class TimeConstraint
{
    [JsonPropertyName("start_time")]
    public TimeSpan? StartTime { get; set; } // e.g., 11:00:00

    [JsonPropertyName("end_time")]
    public TimeSpan? EndTime { get; set; } // e.g., 15:00:00
}

public class MarketIntentGroup
{
    [JsonPropertyName("match_count")]
    public int MatchCount { get; set; }

    [JsonPropertyName("markets")]
    public List<string> Markets { get; set; } = new();
}
