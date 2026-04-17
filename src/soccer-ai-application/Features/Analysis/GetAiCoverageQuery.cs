using Mediator.Net.Contracts;
using System.Collections.Generic;

namespace SoccerAi.Application.Features.Analysis;

public class GetAiCoverageQuery : IRequest
{
    public int DaysAhead { get; init; } = 5;
}

public class GetAiCoverageResponse : IResponse
{
    public List<AiCoverageDto> Coverage { get; init; } = new();
}

public record AiCoverageDto(string Date, int TotalMatches, int AnalyzedMatches, int PendingMatches);
