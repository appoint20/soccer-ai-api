using System.Text.Json.Serialization;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Models;

public class MatchAnalysisDto
{
    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = string.Empty;

    public DateTime MatchDate { get; set; }

    public double HomeGoalsAvg { get; set; }
    public double AwayGoalsAvg { get; set; }
    public string ExpectedGoals { get; set; }
    
    [JsonPropertyName("league")]
    public string? League { get; set; }
    
    [JsonPropertyName("probabilities")]
    public PoissonProbabilitiesDto Probabilities { get; set; } = new();
    
    [JsonPropertyName("odds")]
    public MatchOddsDto? Odds { get; set; }
    
    [JsonPropertyName("recommended_market")]
    public string? RecommendedMarket { get; set; }
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
