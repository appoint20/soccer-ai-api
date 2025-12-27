using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public record PoissonProbabilitiesDto
{
    [JsonPropertyName("home_win")]
    public double HomeWin { get; set; }
    
    [JsonPropertyName("draw")]
    public double Draw { get; set; }
    
    [JsonPropertyName("away_win")]
    public double AwayWin { get; set; }
    
    [JsonPropertyName("over_1_5")]
    public double Over15 { get; set; }
    
    [JsonPropertyName("over_2_5")]
    public double Over25 { get; set; }
    
    [JsonPropertyName("btts")]
    public double BTTS { get; set; }
    
    [JsonPropertyName("expected_goals_home")]
    public double ExpectedGoalsHome { get; set; }
    
    [JsonPropertyName("expected_goals_away")]
    public double ExpectedGoalsAway { get; set; }
    
    [JsonPropertyName("prob_2to3_goals")]
    public double Prob2to3Goals { get; set; }
};