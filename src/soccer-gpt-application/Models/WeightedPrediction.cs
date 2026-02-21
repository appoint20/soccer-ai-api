using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

/// <summary>
/// Internal weighted prediction result
/// </summary>
public sealed class WeightedPrediction
{
    [JsonPropertyName("over25")]
    public bool Over25 { get; init; }
    [JsonIgnore]
    public double Over25Prob { get; init; }
    
    [JsonPropertyName("btts")]
    public bool BTTS { get; init; }
    [JsonIgnore]
    public double BTTSProb { get; init; }
    
    [JsonPropertyName("two_to_three_goals")]
    public bool TwoToThreeGoals { get; init; }
    [JsonIgnore]
    public double TwoToThreeGoalsProb { get; init; }
    
    [JsonPropertyName("match_winner")]
    public string MatchWinner { get; init; } = string.Empty; // "home", "draw", "away"
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } // HDA Confidence
}
