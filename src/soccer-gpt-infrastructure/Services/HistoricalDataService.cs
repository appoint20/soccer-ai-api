using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Database-backed historical data service.
/// This service is the single runtime source of historical data and does not read Excel/CSV files.
/// </summary>
public sealed class HistoricalDataService(
    IApplicationDbContext dbContext,
    ILogger<HistoricalDataService> logger) : IHistoricalDataService
{
    private static readonly Dictionary<int, string> LeagueDivisionCodes = new()
    {
        { 39, "E0" },   // Premier League
        { 40, "E1" },   // Championship
        { 41, "E2" },   // League One
        { 42, "E3" },   // League Two
        { 61, "F1" },   // Ligue 1
        { 62, "F2" },   // Ligue 2
        { 78, "D1" },   // Bundesliga
        { 79, "D2" },   // 2. Bundesliga
        { 135, "I1" },  // Serie A
        { 136, "I2" },  // Serie B
        { 140, "SP1" }, // La Liga
        { 141, "SP2" }  // La Liga 2
    };

    public string GetDivisionCode(int leagueId)
    {
        return LeagueDivisionCodes.TryGetValue(leagueId, out var code) ? code : string.Empty;
    }

    public async Task<HistoricalMatchData?> FindMatchAsync(
        string homeTeam,
        string awayTeam,
        DateTime date,
        int leagueId)
    {
        var homeTeamId = await ResolveTeamApiIdAsync(homeTeam, leagueId);
        var awayTeamId = await ResolveTeamApiIdAsync(awayTeam, leagueId);

        if (homeTeamId is null || awayTeamId is null)
        {
            logger.LogDebug("Could not resolve teams for historical lookup: {Home} vs {Away}", homeTeam, awayTeam);
            return null;
        }

        var start = date.Date;
        var end = start.AddDays(1);

        var fixture = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.LeagueId == leagueId
                        && f.Status == "FT"
                        && f.Date >= start
                        && f.Date < end
                        && f.HomeTeamId == homeTeamId.Value
                        && f.AwayTeamId == awayTeamId.Value)
            .OrderByDescending(f => f.Date)
            .FirstOrDefaultAsync();

        if (fixture is null)
            return null;

        var names = await LoadTeamNameMapAsync(new[] { fixture.HomeTeamId, fixture.AwayTeamId });
        return ToHistoricalMatchData(fixture, names);
    }

    public async Task<List<HistoricalMatchData>> GetTeamHistoryAsync(
        string teamName,
        int leagueId,
        DateTime beforeDate,
        int limit = 6)
    {
        var teamId = await ResolveTeamApiIdAsync(teamName, leagueId);
        if (teamId is null)
            return [];

        var fixtures = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.LeagueId == leagueId
                        && f.Status == "FT"
                        && f.Date < beforeDate
                        && (f.HomeTeamId == teamId.Value || f.AwayTeamId == teamId.Value))
            .OrderByDescending(f => f.Date)
            .Take(limit)
            .ToListAsync();

        if (fixtures.Count == 0)
            return [];

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct();
        var names = await LoadTeamNameMapAsync(teamIds);

        return fixtures.Select(f => ToHistoricalMatchData(f, names)).ToList();
    }

    public async Task<Dictionary<string, int>> GetAvailableDivisionsAsync()
    {
        var rows = await dbContext.Fixtures.AsNoTracking()
            .GroupBy(f => f.LeagueId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        var result = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            var code = GetDivisionCode(row.Key);
            result[string.IsNullOrWhiteSpace(code) ? $"L{row.Key}" : code] = row.Count;
        }

        return result;
    }

    private async Task<int?> ResolveTeamApiIdAsync(string rawTeamName, int leagueId)
    {
        var normalized = NormalizeName(rawTeamName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var teams = await dbContext.Teams.AsNoTracking()
            .Where(t => t.LeagueId == leagueId)
            .Select(t => new { t.ApiId, t.Name })
            .ToListAsync();

        // Exact normalized match first.
        var exact = teams.FirstOrDefault(t => NormalizeName(t.Name) == normalized);
        if (exact is not null)
            return exact.ApiId;

        // Fallback: contains match (small safety net for slight naming differences).
        var contains = teams.FirstOrDefault(t =>
            NormalizeName(t.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(NormalizeName(t.Name), StringComparison.OrdinalIgnoreCase));

        return contains?.ApiId;
    }

    private async Task<Dictionary<int, string>> LoadTeamNameMapAsync(IEnumerable<int> teamApiIds)
    {
        var ids = teamApiIds.Distinct().ToList();
        return await dbContext.Teams.AsNoTracking()
            .Where(t => ids.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name);
    }

    private static HistoricalMatchData ToHistoricalMatchData(Fixture fixture, IReadOnlyDictionary<int, string> teamNames)
    {
        return new HistoricalMatchData
        {
            Date = fixture.Date,
            HomeTeam = teamNames.GetValueOrDefault(fixture.HomeTeamId, $"Team {fixture.HomeTeamId}"),
            AwayTeam = teamNames.GetValueOrDefault(fixture.AwayTeamId, $"Team {fixture.AwayTeamId}"),
            Fthg = fixture.HomeGoal,
            Ftag = fixture.AwayGoal,
            Hthg = fixture.HtHomeGoal,
            Htag = fixture.HtAwayGoal,
            HomeShots = fixture.HomeShots,
            AwayShots = fixture.AwayShots,
            HomeShotsOnTarget = fixture.HomeShotsOnTarget,
            AwayShotsOnTarget = fixture.AwayShotsOnTarget,
            Division = LeagueDivisionCodes.GetValueOrDefault(fixture.LeagueId, $"L{fixture.LeagueId}"),
            HomeWinOdds = fixture.HomeWinOdds,
            DrawOdds = fixture.DrawOdds,
            AwayWinOdds = fixture.AwayWinOdds,
            Over25Odds = fixture.Over25Odds,
            Under25Odds = fixture.Under25Odds
        };
    }

    private static string NormalizeName(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(" fc", string.Empty)
            .Replace(" afc", string.Empty)
            .Replace(".", string.Empty)
            .Replace("-", " ")
            .Replace("  ", " ");
    }
}
