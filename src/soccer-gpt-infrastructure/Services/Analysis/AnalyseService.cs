using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.Analysis;

/// <summary>
/// Centralized service for match analysis
/// Orchestrates existing services to provide comprehensive match analysis
/// </summary>
public class AnalyseService(
    IHistoricalDataRepository historicalData,
    IAdvancedStatsService advancedStats,
    IDecisionService decisionService,
    ILogger<AnalyseService> logger)
    : IAnalyseService
{
    private readonly IDecisionService _decisionService = decisionService;

    public async Task<MatchAnalysisDto> AnalyzeMatchAsync(
        string homeTeam,
        string awayTeam,
        string? league = null,
        MatchOddsDto? odds = null)
    {
        logger.LogInformation("Analyzing match: {Home} vs {Away} ({League})", 
            homeTeam, awayTeam, league ?? "Unknown");
        
        try
        {
            // 1. Load historical data
            var allHistory = await LoadHistoricalDataAsync(homeTeam, awayTeam, league);
            
            // 2. Calculate analytics using existing AdvancedStatsService
            var analytics = await advancedStats.CalculateAnalyticsAsync(
                homeTeam,
                awayTeam,
                allHistory,
                league,
                null); // No ML prediction
            
            // 3. Build comprehensive analysis
            return new MatchAnalysisDto
            {
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                League = league,
                HomeGoalsAvg = CalculateTeamGoalsAverage(homeTeam, allHistory, true),
                AwayGoalsAvg = CalculateTeamGoalsAverage(awayTeam, allHistory, false),
                Probabilities = analytics.Probabilities,
                ExpectedHomeGoals = analytics.Probabilities.ExpectedGoalsHome,
                ExpectedAwayGoals = analytics.Probabilities.ExpectedGoalsAway,
                Decision = analytics.Decision,
                Odds = odds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing match: {Home} vs {Away}", homeTeam, awayTeam);
            
            // Return minimal analysis on error
            return new MatchAnalysisDto
            {
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                League = league,
                Odds = odds,
                Decision = new BettingDecisionDto 
                { 
                    SelectedMarket = "No Bet",
                    Reasons = [$"Analysis error: {ex.Message}"]
                }
            };
        }
    }
    
    public async Task<List<MatchAnalysisDto>> AnalyzeMatchesAsync(List<MatchFixtureDto> fixtures)
    {
        logger.LogInformation("Analyzing {Count} matches in batch", fixtures.Count);
        
        var analyses = new List<MatchAnalysisDto>();
        
        foreach (var fixture in fixtures)
        {
            try
            {
                var analysis = await AnalyzeMatchAsync(
                    fixture.HomeTeam,
                    fixture.AwayTeam,
                    fixture.League,
                    fixture.Odds);
                
                if (fixture.MatchDate.HasValue)
                {
                    analysis.MatchDate = fixture.MatchDate;
                }
                
                analyses.Add(analysis);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to analyze match: {Home} vs {Away}", 
                    fixture.HomeTeam, fixture.AwayTeam);
                // Continue with other matches
            }
        }
        
        logger.LogInformation("Successfully analyzed {Count}/{Total} matches", 
            analyses.Count, fixtures.Count);
        
        return analyses;
    }
    
    private async Task<List<HistoricalMatchDto>> LoadHistoricalDataAsync(
        string homeTeam, 
        string awayTeam, 
        string? league)
    {
        // Get all matches and filter for relevant teams
        var allMatches = await historicalData.GetAllMatchesAsync();
        
        return allMatches
            .Where(m => m.HomeTeam == homeTeam || m.AwayTeam == homeTeam ||
                       m.HomeTeam == awayTeam || m.AwayTeam == awayTeam)
            .Where(m => string.IsNullOrEmpty(league) || m.League == league)
            .ToList();
    }
    
    private double CalculateTeamGoalsAverage(string teamName, List<HistoricalMatchDto> history, bool isHome)
    {
        // Filter matches where the team played in the specified venue (home or away)
        var teamMatches = history
            .Where(m => isHome 
                ? m.HomeTeam == teamName  // When isHome=true, get matches where team played at home
                : m.AwayTeam == teamName) // When isHome=false, get matches where team played away
            .OrderByDescending(m => m.Date)
            .Take(5) // Last 5 matches in that venue
            .ToList();
        
        if (!teamMatches.Any()) return 0.0;
        
        double totalGoals = 0;
        foreach (var match in teamMatches)
        {
            // Since we already filtered by venue, we know:
            // - If isHome=true, team is always home team, so count FTHG
            // - If isHome=false, team is always away team, so count FTAG
            totalGoals += isHome ? match.FTHG : match.FTAG;
        }
        
        return totalGoals / teamMatches.Count;
    }
}
