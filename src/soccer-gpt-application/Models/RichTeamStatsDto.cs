using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public record RichTeamStatsDto
{
    [JsonPropertyName("team_name")]
    public string TeamName { get; init; } = string.Empty;
    
    [JsonPropertyName("avg_goals_for")]
    public double AvgGoalsFor { get; init; }
    
    [JsonPropertyName("avg_goals_against")]
    public double AvgGoalsAgainst { get; init; }
    
    // Dominance
    [JsonPropertyName("avg_shots")]
    public double AvgShots { get; init; }
    
    [JsonPropertyName("avg_shots_on_target")]
    public double AvgShotsOnTarget { get; init; }
    
    [JsonPropertyName("avg_corners")]
    public double AvgCorners { get; init; }
    
    [JsonPropertyName("shot_conversion_rate")]
    public double ShotConversionRate { get; init; } // Goals / ShotsOnTarget
    
    // Form
    [JsonPropertyName("form_last_5")]
    public string FormLast5 { get; init; } = string.Empty;
    
    [JsonPropertyName("win_rate_last_10")]
    public double WinRateLast10 { get; init; }
    
    // Style
    [JsonPropertyName("btts_percentage")]
    public double BTTSPercentage { get; init; }
    
    [JsonPropertyName("over_25_percentage")]
    public double Over25Percentage { get; init; }
    
    [JsonPropertyName("clean_sheet_percentage")]
    public double CleanSheetPercentage { get; init; }
    
    [JsonPropertyName("failed_to_score_percentage")]
    public double FailedToScorePercentage { get; init; }
}
