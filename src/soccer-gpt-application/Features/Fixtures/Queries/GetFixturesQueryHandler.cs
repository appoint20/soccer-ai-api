using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Fixtures.Queries;

public class GetFixturesQueryHandler(
    IApplicationDbContext dbContext) : IRequestHandler<GetFixturesQuery, GetFixturesResponse>
{
    public async Task<GetFixturesResponse> Handle(
        IReceiveContext<GetFixturesQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;
        var dbQuery = dbContext.Fixtures.AsNoTracking();

        dbQuery = query.Upcoming 
            ? dbQuery.Where(f => f.Date >= DateTime.Today && !f.Played) 
            : dbQuery.Where(f => f.Date < DateTime.Today || f.Played);

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var fixtures = await dbQuery
            .OrderBy(f => f.Date)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(f => new FixtureDto
            {
                Id = f.Id,
                Date = f.Date,
                Time = f.Time.ToString(@"hh\:mm"),
                HomeTeam = f.HomeName,
                AwayTeam = f.AwayName,
                League = f.LeagueName,
                HomeWinOdds = f.HomeOdds,
                DrawOdds = f.DrawOdds,
                AwayWinOdds = f.AwayOdds,
                Over25Odds = f.Over25Odds,
                BttsOdds = f.BttsOdds
            })
            .ToListAsync(cancellationToken);

        return new GetFixturesResponse
        {
            Data = new PagedResponse<FixtureDto>
            {
             Limit   = query.Limit,
             Items = fixtures,
             Total = totalCount,
             Offset = query.Offset
            }
        };
    }
}
