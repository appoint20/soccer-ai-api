using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

public sealed class PredictionResponse
{
    [JsonPropertyName("over25")]
    public BoolPrediction Over25 { get; init; } = new();

    [JsonPropertyName("btts")]
    public BoolPrediction BTTS { get; init; } = new();

    [JsonPropertyName("two_to_three_goals")]
    public BoolPrediction TwoToThreeGoals { get; init; } = new();

    [JsonPropertyName("low_scoring")]
    public BoolPrediction LowScoring { get; init; } = new();

    [JsonPropertyName("home_win")]
    public BoolPrediction HomeWin { get; init; } = new();

    [JsonPropertyName("draw")]
    public BoolPrediction Draw { get; init; } = new();

    [JsonPropertyName("away_win")]
    public BoolPrediction AwayWin { get; init; } = new();

    [JsonPropertyName("match_winner")]
    public StringPrediction MatchWinner { get; init; } = new();
}

public sealed class BoolPrediction
{
    [JsonPropertyName("prediction")]
    public bool Prediction { get; init; }

    [JsonPropertyName("probability")]
    public double Probability { get; init; }

    [JsonPropertyName("is_qualified")]
    public bool IsQualified { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class StringPrediction
{
    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("is_qualified")]
    public bool IsQualified { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
