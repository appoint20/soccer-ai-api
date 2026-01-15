using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis.Queries;

public class GetUpcomingMatchesQueryHandler(
    IApplicationDbContext dbContext,
    IAnalyzeService analyzeService): IRequestHandler<GetUpcomingMatchesQuery, GetUpcomingMatchesResponse>
{
    public async Task<GetUpcomingMatchesResponse> Handle(
        IReceiveContext<GetUpcomingMatchesQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;

        var analysedMatches = await analyzeService.AnalyzeBy();
      
        return new GetUpcomingMatchesResponse
        {
            Data = new PagedResponse<UpcomingMatchDto>
            {
                Offset = query.Offset,
                Limit = query.Limit,
                Total = total,
                Items = enrichedMatches,
            }
        };
    }
}
