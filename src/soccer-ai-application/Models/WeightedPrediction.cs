using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

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
    
    [JsonIgnore]
    public double HomeProb { get; init; }
    [JsonIgnore]
    public double DrawProb { get; init; }
    [JsonIgnore]
    public double AwayProb { get; init; }

    [JsonPropertyName("match_winner")]
    public string MatchWinner { get; init; } = string.Empty; // "home", "draw", "away"
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } // HDA Confidence
}
