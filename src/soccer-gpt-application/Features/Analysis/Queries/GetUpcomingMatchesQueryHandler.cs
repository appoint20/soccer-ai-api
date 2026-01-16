using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis.Queries;

public class GetUpcomingMatchesQueryHandler(IAnalyzeService analyzeService) 
    : IRequestHandler<GetUpcomingMatchesQuery, GetUpcomingMatchesResponse>
{
    public async Task<GetUpcomingMatchesResponse> Handle(
        IReceiveContext<GetUpcomingMatchesQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;

        var analysedMatches = await analyzeService.AnalyzeUpcomingAsync(
            query.Date, query.Offset, query.Limit);
      
        return new GetUpcomingMatchesResponse
        {
            Data = new PagedResponse<AnalysisDto>
            {
                Offset = query.Offset,
                Limit = query.Limit,
                Total = analysedMatches.Count,
                Items = analysedMatches
            }
        };
    }
}
