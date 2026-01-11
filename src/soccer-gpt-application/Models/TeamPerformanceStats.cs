using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public record TeamPerformanceStats
{
    [JsonPropertyName("matches_played")]
    public int MatchesPlayed { get; init; }

    [JsonPropertyName("goals_scored")]
    public int GoalsScored { get; init; }

    [JsonPropertyName("goals_conceded")]
    public int GoalsConceded { get; init; }

    [JsonPropertyName("goals_scored_avg")]
    public double GoalsScoredAvg { get; init; }

    [JsonPropertyName("goals_conceded_avg")]
    public double GoalsConcededAvg { get; init; }

    [JsonPropertyName("over_25_percentage")]
    public double Over25Percentage { get; init; } // Ratio 0.0-1.0

    [JsonPropertyName("btts_percentage")]
    public double BTTSPercentage { get; init; }   // Ratio 0.0-1.0

    [JsonPropertyName("goals_2_to_3_percentage")]
    public double Goals2To3Percentage { get; init; } // Ratio 0.0-1.0
}
