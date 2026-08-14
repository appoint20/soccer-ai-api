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
