using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Matches.Queries;

public class GetUpcomingMatchesResponse : IResponse
{
    public PagedResponse<UpcomingMatchDto> Data { get; init; } = new();
}