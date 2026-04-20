using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// Simplified match data for the AI Portfolio Architect to reduce token usage.
/// </summary>
public sealed class CombinationMatchInput
{
    [JsonPropertyName("match_id")]
    public int MatchId { get; set; }

    [JsonPropertyName("teams")]
    public string Teams { get; set; } = string.Empty;

    [JsonPropertyName("league")]
    public string League { get; set; } = string.Empty;

    [JsonPropertyName("date_time")]
    public DateTimeOffset DateTime { get; set; }

    [JsonPropertyName("odds")]
    public MatchOddsInput Odds { get; set; } = new();

    [JsonPropertyName("predictions")]
    public MatchPredictionsInput Predictions { get; set; } = new();

    [JsonPropertyName("ai_judgement")]
    public AiJudgementInput AiJudgement { get; set; } = new();
}

public sealed class MatchOddsInput
{
    public double? Home { get; set; }
    public double? Draw { get; set; }
    public double? Away { get; set; }
    public double? Over25 { get; set; }
    public double? Btts { get; set; }
}

public sealed class MatchPredictionsInput
{
    public MarketPredictionInput Btts { get; set; } = new();
    public MarketPredictionInput Over25 { get; set; } = new();
    public MarketPredictionInput HomeWin { get; set; } = new();
    public MarketPredictionInput AwayWin { get; set; } = new();
    public MarketPredictionInput Goals23 { get; set; } = new();
}

public sealed class MarketPredictionInput
{
    [JsonPropertyName("prediction")]
    public bool Prediction { get; set; }

    [JsonPropertyName("probability")]
    public double Probability { get; set; }
}

public sealed class AiJudgementInput
{
    [JsonPropertyName("recommendation")]
    public string Recommendation { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }

    [JsonPropertyName("is_trap")]
    public bool IsTrap { get; set; }
}
