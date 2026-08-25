using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Services;

namespace SoccerAi.Application.Features.Leagues;

public class GetLeagueStatusHandler(IApplicationDbContext dbContext) : IRequestHandler<GetLeagueStatusQuery, GetLeagueStatusResponse>
{
    public async Task<GetLeagueStatusResponse> Handle(IReceiveContext<GetLeagueStatusQuery> context, CancellationToken ct)
    {
        var leagueId = context.Message.LeagueId;
        var currentSeason = DateTimeOffset.UtcNow.Month >= 7 ? DateTimeOffset.UtcNow.Year : DateTimeOffset.UtcNow.Year - 1;

        var fixtureStats = await dbContext.Fixtures
            .Where(f => f.LeagueId == leagueId)
            .GroupBy(f => 1)
            .Select(g => new {
                Total = g.Count(),
                CurrentSeasonCount = g.Count(f => f.IsCurrentSeason),
                LastDate = g.Max(f => (DateTimeOffset?)f.Date),
                LastUpdatedAt = g.Max(f => (DateTimeOffset?)f.UpdatedAt ?? (DateTimeOffset?)f.CreatedAt)
            })
            .FirstOrDefaultAsync(ct);

        var teamCount = await dbContext.Teams
            .CountAsync(t => t.LeagueId == leagueId, ct);

        // Simple way to check if standings were synced (rank > 0)
        var hasStandings = await dbContext.Teams
            .AnyAsync(t => t.LeagueId == leagueId && t.Rank > 0, ct);

        return new GetLeagueStatusResponse
        {
            LeagueId = leagueId,
            TotalFixtures = fixtureStats?.Total ?? 0,
            CurrentSeasonFixtures = fixtureStats?.CurrentSeasonCount ?? 0,
            LastFixtureDate = fixtureStats?.LastDate,
            LastUpdatedAt = fixtureStats?.LastUpdatedAt,
            TeamCount = teamCount,
            HasStandings = hasStandings,
            LeagueName = LeagueCatalog.Name(leagueId)
        };
    }

}
