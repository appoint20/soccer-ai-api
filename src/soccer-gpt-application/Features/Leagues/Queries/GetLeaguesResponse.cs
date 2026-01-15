using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Leagues.Queries;

public class GetLeaguesResponse : IResponse
{
    public PagedResponse<string> Data { get; init; } = new();
}
