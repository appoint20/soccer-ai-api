
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Leagues.Queries;

public class GetLeaguesQueryHandler : IRequestHandler<GetLeaguesQuery, GetLeaguesResponse>
{
    private readonly ILeaguesRepository _repository;

    public GetLeaguesQueryHandler(ILeaguesRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLeaguesResponse> Handle(IReceiveContext<GetLeaguesQuery> context, CancellationToken cancellationToken)
    {
        var leagues = await _repository.GetLeaguesAsync(cancellationToken);
        
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
