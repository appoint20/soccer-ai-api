using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.HistoricalMatches.Query;

public class GetHistoricalMatchesQuery : IRequest
{
    public string LeagueCode { get; set; } = string.Empty;
    public bool? CurrentSeason { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 20;
}

public class GetHistoricalMatchesResponse : IResponse
{
    public PagedResponse<HistoricalMatchDto> Data { get; init; } = new();
}
