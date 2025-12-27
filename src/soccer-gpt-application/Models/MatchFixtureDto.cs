using System.Text.Json.Serialization;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Models;

public class MatchFixtureDto
{
    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("league")]
    public string League { get; set; } = string.Empty;
    
    [JsonPropertyName("match_date")]
    public DateTime? MatchDate { get; set; }
    
    [JsonPropertyName("odds")]
    public MatchOddsDto? Odds { get; set; }
}
