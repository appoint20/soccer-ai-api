using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis.Queries;

public class GetUpcomingMatchesResponse : IResponse
{
    public PagedResponse<AnalysisDto> Data { get; init; } = new();
}