using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Entities;

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
            LeagueName = GetLeagueName(leagueId)
        };
    }

    private static string GetLeagueName(int leagueId)
    {
        return leagueId switch
        {
            39 => "Premier League",
            40 => "Championship",
            41 => "League One",
            42 => "League Two",
            78 => "Bundesliga",
            79 => "2. Bundesliga",
            80 => "3. Liga",
            135 => "Serie A",
            136 => "Serie B",
            140 => "La Liga",
            141 => "La Liga 2",
            61 => "Ligue 1",
            62 => "Ligue 2",
            34 or 43 or 46 or 154 => "English National League",
            2 => "UEFA Champions League",
            3 => "UEFA Europa League",
            _ => $"Unknown League ({leagueId})"
        };
    }
}
