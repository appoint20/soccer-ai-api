using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Matches.Queries;

public class GetHistoricalMatchesQueryHandler(IApplicationDbContext dbContext) 
    : IRequestHandler<GetHistoricalMatchesQuery, GetHistoricalMatchesResponse>
{
    public async Task<GetHistoricalMatchesResponse> Handle(
        IReceiveContext<GetHistoricalMatchesQuery> context, CancellationToken cancellationToken)
    {
        var query = dbContext.Matches.AsQueryable();

        if (!string.IsNullOrWhiteSpace(context.Message.LeagueCode))
            query = query.Where(m => m.LeagueName == context.Message.LeagueCode);

        if (context.Message.CurrentSeason.HasValue)
            query = query.Where(m => m.CurrentSeason == context.Message.CurrentSeason.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var matches = await query
            .OrderByDescending(m => m.Date)
            .Skip((context.Message.Page - 1) * context.Message.Limit)
            .Take(context.Message.Limit)
            .Select(m => new HistoricalMatchDto
            {
                Date = m.Date,
                Time = m.Time.ToString(),
                HomeTeam = m.HomeTeam.Name,
                AwayTeam = m.AwayTeam.Name,
                FTHG = m.FullTimeHomeGoal,
                FTAG = m.FullTimeAwayGoal,
                FTR = m.FullTimeResult,
                HTHG = m.HalfTimeHomeGoal,
                HTAG = m.HalfTimeAwayGoal,
                HTR = m.HalfTimeResult,
                Div = m.LeagueName, 
                Referee = m.Referee
            })
            .ToListAsync(cancellationToken);

        return new GetHistoricalMatchesResponse
        {
            Data = new PagedResponse<HistoricalMatchDto>
            {
                Offset = context.Message.Page,
                Limit = context.Message.Limit,
                Total = totalCount,
                Items = matches,
            }
        };
    }
}
