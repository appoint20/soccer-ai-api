using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Leagues.Queries;

public class GetLeaguesQueryHandler(): IRequestHandler<GetLeaguesQuery, GetLeaguesResponse>
{
    private readonly List<string> _leagues =
    [
        "Premier League",
        "Championship",
        "League One",
        "League Two",
        "Bundesliga",
        "Serie A",
        "Serie B",
        "La Liga",
        "Ligue 1",
        "Ligue 2"
    ];
    
    public Task<GetLeaguesResponse> Handle(
        IReceiveContext<GetLeaguesQuery> context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GetLeaguesResponse 
        { 
            Data = new PagedResponse<string>
            {
                Items = _leagues,
                Total = _leagues.Count,
                Offset = 0,
                Limit = _leagues.Count
            }
        });
    }
}
