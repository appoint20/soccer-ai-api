using Mediator.Net.Contracts;
using SoccerAi.Application.Models;
using System.Text.Json.Serialization;

namespace SoccerAi.Application.Features.Analysis;

public class GetAiCoverageQuery : PageRequest, IRequest
{
    public int DaysAhead { get; set; } = 5;
}

public class GetAiCoverageResponse : PagedResponse<AiCoverageDto>, IResponse;

public record AiCoverageDto(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("total_matches")] int TotalMatches,
    [property: JsonPropertyName("analyzed_matches")] int AnalyzedMatches,
    [property: JsonPropertyName("pending_matches")] int PendingMatches);
