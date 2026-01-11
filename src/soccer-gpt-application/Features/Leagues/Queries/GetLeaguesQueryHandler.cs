using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Leagues.Queries;

public class GetLeaguesQueryHandler(ILeaguesRepository repository): IRequestHandler<GetLeaguesQuery, GetLeaguesResponse>
{
    public async Task<GetLeaguesResponse> Handle(
        IReceiveContext<GetLeaguesQuery> context, CancellationToken cancellationToken)
    {
        var leagues = await repository.GetLeaguesAsync(cancellationToken);
        
        return new GetLeaguesResponse 
        { 
            Data = new PagedResponse<LeagueDto>
            {
                Items = leagues,
                Total = leagues.Count,
                Offset = 0,
                Limit = leagues.Count
            }
        };
    }
}
