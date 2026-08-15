using System.Text.Json.Serialization;
using Mediator.Net.Contracts;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Analysis;

public class GetMatchAnalysisQuery : PageRequest, IRequest
{
    public DateTimeOffset? Date { get; set; }
    public string Language { get; set; } = "en";
    public bool OnlyAnalyzed { get; set; }
    public bool Refresh { get; set; }
}

/// <summary>
/// Paged analysis for a date.
///
/// Carries the canonical envelope plus the original <c>matches</c> and
/// <c>total_count</c> keys, which are deprecated and duplicate
/// <see cref="Items"/> and <see cref="Total"/>. They exist so the shipped app
/// keeps working across this deploy; drop them once no client reads them.
/// </summary>
public class GetMatchAnalysisResponse : IResponse
{
    [JsonPropertyName("items")]
    public List<MatchAnalysis> Items { get; set; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore => Offset + Items.Count < Total;

    /// <summary>Deprecated — use <see cref="Items"/>.</summary>
    [JsonPropertyName("matches")]
    public List<MatchAnalysis> Matches => Items;

    /// <summary>Deprecated — use <see cref="Total"/>.</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount => Total;

    /// <summary>
    /// Independent language-model forecasts for the fixtures on this page,
    /// keyed by fixture id. Shown alongside the pipeline's own numbers so the
    /// two can be compared; never used to select or price a bet.
    /// </summary>
    [JsonPropertyName("model_forecasts")]
    public Dictionary<int, List<MatchModelForecastDto>> ModelForecasts { get; set; } = [];

    /// <summary>
    /// Accuracy over the finished fixtures on this page only. It is not a
    /// day-wide figure, and with paging on by default it never was one for a
    /// caller reading a single page.
    /// </summary>
    [JsonPropertyName("summary")]
    public AnalysisSummary? Summary { get; set; }
}

public class AnalysisSummary
{
    [JsonPropertyName("total_matches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("correct_matches")]
    public int CorrectMatches { get; set; }

    [JsonPropertyName("accuracy_rate")]
    public double AccuracyRate { get; set; }
}

/// <summary>One model's goals forecast for a fixture, as shown next to the analysis.</summary>
public sealed record MatchModelForecastDto
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("expected_goals")] public required double ExpectedGoals { get; init; }
    [JsonPropertyName("predicted_score")] public required string PredictedScore { get; init; }
    [JsonPropertyName("over_2_5_probability")] public required double Over25Probability { get; init; }
    [JsonPropertyName("btts_probability")] public required double BttsProbability { get; init; }
    [JsonPropertyName("confidence")] public required double Confidence { get; init; }
    [JsonPropertyName("rationale")] public required string Rationale { get; init; }
    [JsonPropertyName("predicted_at_utc")] public required DateTimeOffset PredictedAtUtc { get; init; }

    /// <summary>The pipeline's probability for the same market, frozen at forecast time.</summary>
    [JsonPropertyName("system_over_2_5_probability")] public required double SystemOver25Probability { get; init; }
    [JsonPropertyName("system_btts_probability")] public required double SystemBttsProbability { get; init; }

    /// <summary>Null until the fixture has finished.</summary>
    [JsonPropertyName("actual_total_goals")] public required int? ActualTotalGoals { get; init; }
}
