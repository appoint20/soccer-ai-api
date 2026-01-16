using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public class AnalyzeService(
    IApplicationDbContext dbContext, 
    ITeamStatsService statsService, 
    ILeagueStatsService leagueStatsService, 
    IPoissonService poissonService) : IAnalyzeService
{
    public async Task<List<AnalysisDto>> AnalyzeUpcomingAsync(DateTime date, int offset = 0, int limit = 50)
    {
        // Compare using Date only (SQLite stores as yyyy-MM-dd HH:mm:ss)
        var targetDate = date.Date;
        
        var fixtures = await dbContext.Fixtures
            .AsNoTracking()
            .Where(x => x.Date.Date == targetDate)
            .ToListAsync();

        // Order client-side (SQLite doesn't support TimeSpan in ORDER BY)
        var orderedFixtures = fixtures
            .OrderBy(x => x.Time)
            .Skip(offset)
            .Take(limit)
            .ToList();

        var results = new List<AnalysisDto>();

        foreach (var fixture in orderedFixtures)
        {
            var analysis = await AnalyzeFixture(fixture);
            results.Add(analysis);
        }

        return results;
    }

    private async Task<AnalysisDto> AnalyzeFixture(Fixture fixture)
    {
        var leagueName = fixture.LeagueName;
        var homeTeam = fixture.HomeName;
        var awayTeam = fixture.AwayName;

        // Get league matches BEFORE the fixture date (historical)
        var leagueMatches = GetHistoricalLeagueMatchesBy(leagueName, fixture.Date);
        var homeHistoricalMatches = GetHistoricalMatchesBy(homeTeam, leagueMatches);
        var awayHistoricalMatches = GetHistoricalMatchesBy(awayTeam, leagueMatches);

        // League averages for Poisson
        var leagueGoalAverages = await leagueStatsService.CalculateLeagueAveragesAsync(leagueName, leagueMatches);

        // Current season stats for Poisson strength calculation
        var homeCurrentSeasonStats = await statsService.CalculateAsync(
            homeTeam, homeHistoricalMatches, new TeamStatsOptions());

        var awayCurrentSeasonStats = await statsService.CalculateAsync(
            awayTeam, awayHistoricalMatches, new TeamStatsOptions());

        // Poisson probabilities
        PoissonProbabilities poisson;
        try
        {
            var strengthFactors = poissonService.Build(homeCurrentSeasonStats, awayCurrentSeasonStats, leagueGoalAverages);
            poisson = poissonService.CalculateProbabilities(strengthFactors);
        }
        catch
        {
            poisson = new PoissonProbabilities(); // Safe default
        }

        // Last 3 Home/Away specific stats
        var homeLast3Home = await statsService.CalculateAsync(
            homeTeam, homeHistoricalMatches, new TeamStatsOptions { LastMatches = 3, HomeOnly = true });

        var awayLast3Away = await statsService.CalculateAsync(
            awayTeam, awayHistoricalMatches, new TeamStatsOptions { LastMatches = 3, HomeOnly = false });

        return new AnalysisDto
        {
            Date = fixture.Date,
            Time = fixture.Time,
            LeagueName = leagueName,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeLastNine = homeCurrentSeasonStats,
            HomeLastThreeAtHome = homeLast3Home,
            AwayLastNine = awayCurrentSeasonStats,
            AwayLastThreeAtAway = awayLast3Away,
            AdvancedAnalytics = poisson
        };
    }

    private static List<Match> GetHistoricalMatchesBy(string teamName, IQueryable<Match> historicalLeagueMatches)
    {
        return historicalLeagueMatches
            .Where(m => m.AwayTeam.Name == teamName || m.HomeTeam.Name == teamName)
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .OrderByDescending(m => m.Date)
            .ToList();
    }

    private IOrderedQueryable<Match> GetHistoricalLeagueMatchesBy(string leagueName, DateTime fixtureDate)
    {
        // Get matches BEFORE the fixture date (not after)
        return dbContext.Matches
            .AsNoTracking()
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => m.Date < fixtureDate && m.LeagueName == leagueName && m.CurrentSeason)
            .OrderByDescending(m => m.Date);
    }
}