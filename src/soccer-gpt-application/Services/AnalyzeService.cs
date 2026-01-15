using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public class AnalyzeService(
    IApplicationDbContext dbContext, 
    ITeamStatsService statsService, 
    ILeagueStatsService leagueStatsService, 
    IPoissonService poissonService): IAnalyzeService
{
    public async Task<object> AnalyzeBy()
    {
        var fixtures = await dbContext.Fixtures
            .AsNoTracking()
            .Where(x => x.Date >= DateTime.Today)
            .ToListAsync();
        
        foreach (var fixture in fixtures)
        {
            var leagueName = fixture.LeagueName;
            var homeTeam = fixture.HomeName;
            var awayTeam = fixture.AwayName;
            var leagueMatches = GetHistoricalLeagueMatchesBy(leagueName, fixture.Date);
            var homeHistoricalMatches =  GetHistoricalMatchesBy(homeTeam, leagueMatches);
            var awayHistoricalMatches = GetHistoricalMatchesBy(awayTeam, leagueMatches);

            var leagueGoalAverages = await leagueStatsService.CalculateLeagueAveragesAsync(leagueName, leagueMatches);
                        
            var homeCurrentSeasonStats = await statsService.CalculateAsync(
                homeTeam,
                homeHistoricalMatches, 
                new TeamStatsOptions()
            );
            
            var awayCurrentSeasonStats = await statsService.CalculateAsync(
                awayTeam,
                awayHistoricalMatches, 
                new TeamStatsOptions()
            );

            var strengthFactors = poissonService.Build(
                homeCurrentSeasonStats, 
                awayCurrentSeasonStats, 
                leagueGoalAverages
            );

            var poisson = poissonService.CalculateProbabilities(strengthFactors);

            var homeStats = await statsService.CalculateAsync(
                homeTeam,
                homeHistoricalMatches, 
                new TeamStatsOptions()
            );
            
            var awayStats = await statsService.CalculateAsync(
                awayTeam,
                awayHistoricalMatches, 
                new TeamStatsOptions()
            );
            
            var homeHomeStats = await statsService.CalculateAsync(
                homeTeam,
                homeHistoricalMatches, 
                new TeamStatsOptions
                {
                    LastMatches = 3,
                    HomeOnly = true
                }
            );
            
            var awayAwayStats = await statsService.CalculateAsync(
                awayTeam,
                awayHistoricalMatches, 
                new TeamStatsOptions
                {
                    LastMatches = 3,
                    HomeOnly = false
                }
            );

        }
        throw new NotImplementedException();
    }


    private static List<Match> GetHistoricalMatchesBy(string teamName, IOrderedQueryable<Match> historicalLeagueMatches)
    {
        var matches = historicalLeagueMatches
            .Where(m => m.AwayTeam.Name == teamName || m.HomeTeam.Name == teamName)
            .OrderByDescending(m => m.Date)
            .ToList();

        return matches;
    }
    
    private IOrderedQueryable<Match> GetHistoricalLeagueMatchesBy(string leagueName, DateTime date)
    {
        var matches = dbContext.Matches
            .AsNoTracking()
            .Where(m => m.Date > date && m.LeagueName == leagueName && m.CurrentSeason)
            .OrderByDescending(m => m.Date);

        return matches;
    }
}