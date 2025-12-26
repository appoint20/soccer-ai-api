using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models.ML;

/// <summary>
/// Comprehensive match analysis including team stats, probabilities, and betting decisions
/// Used by both upcoming matches and ticket generation
/// </summary>
public class MatchAnalysisDto
{
    // Match Information
    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("league")]
    public string? League { get; set; }
    
    [JsonPropertyName("match_date")]
    public DateTime? MatchDate { get; set; }
    
    // Team Statistics (simplified - detailed stats come from MatchProbabilitiesDto)
    [JsonPropertyName("home_goals_avg")]
    public double HomeGoalsAvg { get; set; }
    
    [JsonPropertyName("away_goals_avg")]
    public double AwayGoalsAvg { get; set; }
    
    // Poisson Probabilities (uses existing DTO from IAdvancedStatsService)
    [JsonPropertyName("probabilities")]
    public soccer_gpt_application.Interfaces.MatchProbabilitiesDto Probabilities { get; set; } = new();
    
    // Expected Goals (duplicated for convenience - also in Probabilities)
    [JsonPropertyName("expected_home_goals")]
    public double ExpectedHomeGoals { get; set; }
    
    [JsonPropertyName("expected_away_goals")]
    public double ExpectedAwayGoals { get; set; }
    
    // Betting Decision (uses existing DTO from IDecisionService)
    [JsonPropertyName("decision")]
    public soccer_gpt_application.Interfaces.BettingDecisionDto Decision { get; set; } = new();
    
    // Match Odds (uses existing DTO from IHistoricalDataRepository)
    [JsonPropertyName("odds")]
    public soccer_gpt_application.Interfaces.MatchOddsDto? Odds { get; set; }
}
