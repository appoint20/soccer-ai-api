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

    /// <summary>
    /// Builds the prediction directly from the single calibrated probability
    /// set — no blending, no boosts. Since v3 the draw is a recommendable
    /// 1X2 outcome (three-way argmax).
    /// </summary>
    public static WeightedPrediction FromCalibrated(Interfaces.CalibratedProbabilities c)
    {
        var winner = c.Draw >= c.HomeWin && c.Draw >= c.AwayWin ? "draw"
            : c.AwayWin > c.HomeWin ? "away" : "home";
        var confidence = Math.Max(c.Draw, Math.Max(c.HomeWin, c.AwayWin));

        return new WeightedPrediction
        {
            Over25 = c.Over25 > 0.50,
            Over25Prob = Math.Clamp(c.Over25, 0, 1),
            BTTS = c.Btts > 0.50,
            BTTSProb = Math.Clamp(c.Btts, 0, 1),
            TwoToThreeGoals = c.TwoToThreeGoals > 0.50,
            TwoToThreeGoalsProb = Math.Clamp(c.TwoToThreeGoals, 0, 1),
            HomeProb = Math.Round(c.HomeWin, 2),
            DrawProb = Math.Round(c.Draw, 2),
            AwayProb = Math.Round(c.AwayWin, 2),
            MatchWinner = winner,
            Confidence = Math.Round(confidence, 2)
        };
    }
}
