using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Helpers;

/// <summary>
/// Provides common fixture and team loading operations to eliminate duplication across handlers.
/// Centralizes the fixture/team data fetching logic that was repeated in multiple handlers.
/// </summary>
public class FixtureQueryHelper(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Loads paginated fixtures for a given date along with their team mappings.
    /// </summary>
    public async Task<(List<Fixture> Fixtures, Dictionary<int, Team> Teams, int TotalCount)> GetFixturesWithTeamsAsync(
        DateTimeOffset date,
        int? page = null,
        int? pageSize = null,
        bool onlyAnalyzed = false,
        CancellationToken cancellationToken = default)
    {
        var startUtc = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddDays(1);

        var query = dbContext.Fixtures.Where(f => f.Date >= startUtc && f.Date < endUtc);

        if (onlyAnalyzed)
        {
            query = query.Where(f => dbContext.FixtureAnalyses.Any(a => a.FixtureId == f.Id));
        }
        
        var totalCount = await query.CountAsync(cancellationToken);

        if (page.HasValue && pageSize.HasValue)
        {
            query = query
                .OrderBy(f => f.Date)
                .Skip((page.Value - 1) * pageSize.Value)
                .Take(pageSize.Value);
        }

        var fixtures = await query.ToListAsync(cancellationToken);

        var teamIds = fixtures
            .SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId })
            .Distinct()
            .ToList();

        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        return (fixtures, teams, totalCount);
    }

    /// <summary>
    /// Gets paginated fixtures for verification/admin purposes.
    /// </summary>
    public async Task<(List<Fixture> Fixtures, int TotalCount)> GetPaginatedFixturesAsync(
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var total = await dbContext.Fixtures.CountAsync(cancellationToken);

        var fixtures = await dbContext.Fixtures
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (fixtures, total);
    }

    /// <summary>
    /// Gets paginated teams with optional league filtering.
    /// </summary>
    public async Task<(List<Team> Teams, int TotalCount)> GetPaginatedTeamsAsync(
        int limit = 50,
        int offset = 0,
        int? leagueId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Teams.AsQueryable();

        if (leagueId.HasValue)
            query = query.Where(t => t.LeagueId == leagueId.Value);

        var total = await query.CountAsync(cancellationToken);

        var teams = await query
            .OrderBy(t => t.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (teams, total);
    }

    /// <summary>
    /// Gets team lookup dictionary for a specific fixture.
    /// </summary>
    public async Task<Dictionary<int, string>> GetTeamNamesForFixuresAsync(
        List<Fixture> fixtures,
        Dictionary<int, Team>? teams = null,
        CancellationToken cancellationToken = default)
    {
        if (teams == null)
        {
            var teamIds = fixtures
                .SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId })
                .Distinct()
                .ToList();

            teams = await dbContext.Teams
                .Where(t => teamIds.Contains(t.ApiId))
                .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);
        }

        return teams.ToDictionary(
            x => x.Key,
            x => x.Value.Name ?? $"Team {x.Key}");
    }
}
