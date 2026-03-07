using Mediator.Net.Contracts;
using System.Collections.Generic;

namespace SoccerAi.Application.Features.Analysis;

public class GetGeminiCoverageQuery : IRequest
{
    public int DaysAhead { get; init; } = 5;
}

public class GetGeminiCoverageResponse : IResponse
{
    public List<GeminiCoverageDto> Coverage { get; init; } = new();
}

public record GeminiCoverageDto(string Date, int TotalMatches, int AnalyzedMatches, int PendingMatches);
