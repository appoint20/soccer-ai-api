using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Services;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Service for syncing fixtures from API-Football.
/// </summary>
public class FixtureSyncService(IApiFootballService apiService, 
    IHistoricalDataService historicalService, IApplicationDbContext dbContext, ILogger<FixtureSyncService> logger)
{
    private static readonly int[] SupportedLeagues = [ 39, 40, 41, 42, 135, 136, 61, 62, 140, 78, 79, 141 ];

    /// <summary>
    /// Get current football season (starts in July)
    /// </summary>
    private static int GetCurrentSeason() => DateTime.Now.Month >= 7 ? DateTime.Now.Year : DateTime.Now.Year - 1;

    /// <summary>
    /// Check if the season is the current season
    /// </summary>
    private static bool IsCurrentSeason(int season) => season == GetCurrentSeason();

    /// <summary>
    /// Sync last N seasons for all supported leagues
    /// </summary>
    public async Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct)
    {
        var result = new SyncResult();
        var currentSeason = GetCurrentSeason();
        
        logger.LogInformation("Starting multi-season sync for {Count} seasons (from {From} to {To})", 
            numberOfSeasons, currentSeason - numberOfSeasons + 1, currentSeason);

        for (int i = 0; i < numberOfSeasons; i++)
        {
            var season = currentSeason - i;
            var seasonResult = await SyncAllLeaguesAsync(season, ct);
            result.Created += seasonResult.Created;
            result.Updated += seasonResult.Updated;
            result.LeaguesSynced += seasonResult.LeaguesSynced;
            result.Errors += seasonResult.Errors;
        }

        logger.LogInformation(
            "Multi-season sync complete. Total Created: {Created}, Updated: {Updated}, Errors: {Errors}",
            result.Created, result.Updated, result.Errors);

        return result;
    }

    /// <summary>
    /// Sync fixtures for all supported leagues for a given season
    /// </summary>
    public async Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken ct)
    {
        var result = new SyncResult();
        logger.LogInformation("Starting fixture sync for season {Season} (IsCurrentSeason: {IsCurrent})", 
            season, IsCurrentSeason(season));

        foreach (var leagueId in SupportedLeagues)
        {
            try
            {
                var leagueResult = await SyncLeagueFixturesAsync(leagueId, season, ct);
                result.Updated += leagueResult.Updated;
                result.Created += leagueResult.Created;
                result.LeaguesSynced++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync fixtures for league {LeagueId}", leagueId);
                result.Errors++;
            }

            // Rate limiting
            await Task.Delay(100, ct);
        }

        logger.LogInformation(
            "Fixture sync complete. Leagues: {Leagues}, Created: {Created}, Updated: {Updated}, Errors: {Errors}",
            result.LeaguesSynced, result.Created, result.Updated, result.Errors);

        return result;
    }

    /// <summary>
    /// Sync fixtures for a specific league/season
    /// Phase 1: Capture odds for upcoming fixtures (within 14 days)
    /// Phase 2: Update completed fixtures with results (preserve pre-captured odds)
    /// </summary>
    public async Task<SyncResult> SyncLeagueFixturesAsync(int leagueId, int season, CancellationToken ct)
    {
        var result = new SyncResult();
        logger.LogInformation("Syncing fixtures for league {LeagueId} season {Season}", leagueId, season);

        var apiFixtures = await apiService.GetFixturesAsync(leagueId, season);
        
        // Phase 1: Upcoming fixtures (capture pre-match odds before they expire)
        var upcomingFixtures = apiFixtures
            .Where(f => f.StatusShort == "NS" && f.Date > DateTime.UtcNow && f.Date <= DateTime.UtcNow.AddDays(14))
            .ToList();

        foreach (var apiFixture in upcomingFixtures)
        {
            var existingFixture = await dbContext.Fixtures
                .FirstOrDefaultAsync(f => f.ApiId == apiFixture.ApiId, ct);

            if (existingFixture == null)
            {
                // New upcoming fixture - capture odds now
                var fixture = await CreateUpcomingFixtureAsync(apiFixture, leagueId, season);
                if (fixture != null)
                {
                    dbContext.Fixtures.Add(fixture);
                    result.Created++;
                }
            }
            else if (existingFixture.HomeWinOdds == null)
            {
                // Existing but no odds yet - try to fetch
                await UpdateFixtureOddsAsync(existingFixture, apiFixture.ApiId);
                result.Updated++;
            }
        }

        // Phase 2: Completed fixtures (update with results, preserve odds)
        // For recently completed (within 7 days), API still has odds - capture them!
        var completedFixtures = apiFixtures.Where(f => f.StatusShort == "FT").ToList();

        foreach (var apiFixture in completedFixtures)
        {
            var existingFixture = await dbContext.Fixtures
                .FirstOrDefaultAsync(f => f.ApiId == apiFixture.ApiId, ct);

            // Check if match is within 7-day odds window
            var isWithinOddsWindow = apiFixture.Date >= DateTime.UtcNow.AddDays(-7);

            if (existingFixture == null)
            {
                // Completed fixture we never captured - create with full enrichment
                // If within 7 days, we can still get odds from API
                var fixture = await CreateEnrichedFixtureAsync(apiFixture, leagueId, season, ct, fetchOdds: isWithinOddsWindow);
                if (fixture != null)
                {
                    dbContext.Fixtures.Add(fixture);
                    result.Created++;
                }
            }
            else if (existingFixture.Status != "FT")
            {
                // Previously upcoming, now completed - update results, keep odds
                await UpdateCompletedFixtureAsync(existingFixture, apiFixture, leagueId, ct);
                result.Updated++;
            }
            else if (existingFixture.HomeWinOdds == null && isWithinOddsWindow)
            {
                // Existing completed fixture missing odds, still within 7-day window - try to fetch
                await UpdateFixtureOddsAsync(existingFixture, apiFixture.ApiId);
                result.Updated++;
            }

            // Rate limit mitigation (avoid 429)
            await Task.Delay(300, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        result.LeaguesSynced = 1;

        logger.LogInformation(
            "League {LeagueId}: Created {Created}, Updated {Updated} fixtures", 
            leagueId, result.Created, result.Updated);

        return result;
    }

    /// <summary>
    /// Create upcoming fixture with pre-match odds (no results yet)
    /// </summary>
    private async Task<Fixture?> CreateUpcomingFixtureAsync(ApiFixture apiFixture, int leagueId, int season)
    {
        try
        {
            var apiOdds = await apiService.GetFixtureOddsAsync(apiFixture.ApiId);
            
            return new Fixture
            {
                ApiId = apiFixture.ApiId,
                HomeTeamId = apiFixture.HomeTeamApiId,
                AwayTeamId = apiFixture.AwayTeamApiId,
                LeagueId = leagueId,
                Date = apiFixture.Date,
                Status = "NS",
                
                // No results yet
                HomeGoal = 0, AwayGoal = 0, HtHomeGoal = 0, HtAwayGoal = 0,
                HomeGoalAvg = 0, AwayGoalAvg = 0, HtHomeGoalAvg = 0, HtAwayGoalAvg = 0,
                HomeShots = 0, AwayShots = 0, HomeShotsOnTarget = 0, AwayShotsOnTarget = 0,
                HomeXg = 0, AwayXg = 0,
                
                // Pre-match odds - THIS IS WHY WE CAPTURE EARLY
                HomeWinOdds = apiOdds?.HomeWin,
                DrawOdds = apiOdds?.Draw,
                AwayWinOdds = apiOdds?.AwayWin,
                Over25Odds = apiOdds?.Over25,
                Under25Odds = apiOdds?.Under25,
                BttsYesOdds = apiOdds?.BttsYes,
                
                IsCurrentSeason = IsCurrentSeason(season),
                IsDerby = DerbyDetector.IsDerby(apiFixture.HomeTeamApiId, apiFixture.AwayTeamApiId),
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create upcoming fixture {ApiId}", apiFixture.ApiId);
            return null;
        }
    }

    /// <summary>
    /// Update fixture with odds only
    /// </summary>
    private async Task UpdateFixtureOddsAsync(Fixture fixture, int apiId)
    {
        var apiOdds = await apiService.GetFixtureOddsAsync(apiId);
        if (apiOdds != null)
        {
            fixture.HomeWinOdds = apiOdds.HomeWin;
            fixture.DrawOdds = apiOdds.Draw;
            fixture.AwayWinOdds = apiOdds.AwayWin;
            fixture.Over25Odds = apiOdds.Over25;
            fixture.Under25Odds = apiOdds.Under25;
            fixture.BttsYesOdds = apiOdds.BttsYes;
            fixture.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Update previously captured fixture with match results (preserve odds)
    /// </summary>
    private async Task UpdateCompletedFixtureAsync(Fixture fixture, ApiFixture apiFixture, int leagueId, CancellationToken ct)
    {
        var stats = await apiService.GetBothTeamStatsAsync(apiFixture.ApiId);
        
        // Update with results
        fixture.Status = "FT";
        fixture.HomeGoal = apiFixture.HomeGoals ?? 0;
        fixture.AwayGoal = apiFixture.AwayGoals ?? 0;
        fixture.HtHomeGoal = apiFixture.HomeGoalsHalftime ?? 0;
        fixture.HtAwayGoal = apiFixture.AwayGoalsHalftime ?? 0;
        
        // Stats from API
        fixture.HomeShots = stats.Home?.TotalShots ?? 0;
        fixture.AwayShots = stats.Away?.TotalShots ?? 0;
        fixture.HomeShotsOnTarget = stats.Home?.ShotsOnGoal ?? 0;
        fixture.AwayShotsOnTarget = stats.Away?.ShotsOnGoal ?? 0;
        fixture.HomeBallPossession = stats.Home?.BallPossession;
        fixture.AwayBallPossession = stats.Away?.BallPossession;
        fixture.HomePassesAccurate = stats.Home?.PassesAccurate;
        fixture.AwayPassesAccurate = stats.Away?.PassesAccurate;
        fixture.HomeXg = stats.Home?.ExpectedGoals ?? 0;
        fixture.AwayXg = stats.Away?.ExpectedGoals ?? 0;
        
        // Calculate averages from historical data
        var homeHistory = await historicalService.GetTeamHistoryAsync(
            apiFixture.HomeTeamName, leagueId, apiFixture.Date, 6);
        var awayHistory = await historicalService.GetTeamHistoryAsync(
            apiFixture.AwayTeamName, leagueId, apiFixture.Date, 6);

        var avgs = CalculateRollingAverages(homeHistory, awayHistory);
            
        fixture.HomeGoalAvg = avgs.HomeGoalAvg;
        fixture.AwayGoalAvg = avgs.AwayGoalAvg;
        fixture.HtHomeGoalAvg = avgs.HtHomeGoalAvg;
        fixture.HtAwayGoalAvg = avgs.HtAwayGoalAvg;
        
        // NOTE: We preserve the pre-match odds that were captured earlier!
        fixture.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Create an enriched fixture from API data + historical data
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="fetchOdds">If true, fetch odds from API (only valid within 7 days of match)</param>
    /// <param name="apiFixture"></param>
    /// <param name="leagueId"></param>
    /// <param name="season"></param>
    private async Task<Fixture?> CreateEnrichedFixtureAsync(ApiFixture apiFixture, int leagueId, int season, CancellationToken ct, bool fetchOdds = true)
    {
        try
        {
            // 1. Fetch all data in parallel where possible (or sequentially if simple)
            var stats = await apiService.GetBothTeamStatsAsync(apiFixture.ApiId);
            
            var apiOdds = fetchOdds ? await apiService.GetFixtureOddsAsync(apiFixture.ApiId) : null;

            var historicalMatch = await historicalService.FindMatchAsync(
                apiFixture.HomeTeamName, apiFixture.AwayTeamName, apiFixture.Date, leagueId);

            var homeHistory = await historicalService.GetTeamHistoryAsync(
                apiFixture.HomeTeamName, leagueId, apiFixture.Date, 6);
            var awayHistory = await historicalService.GetTeamHistoryAsync(
                apiFixture.AwayTeamName, leagueId, apiFixture.Date, 6);

            // 2. Process data (Domain Logic)
            var averages = CalculateRollingAverages(homeHistory, awayHistory);

            // 3. Build Entity
            return BuildFixtureEntity(apiFixture, leagueId, season, stats, apiOdds, historicalMatch, averages);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create fixture for API ID {ApiId}", apiFixture.ApiId);
            return null;
        }
    }
    private static FixtureAverages CalculateRollingAverages(
        List<HistoricalMatchData> homeHistory, List<HistoricalMatchData> awayHistory)
    {
        return new FixtureAverages
        {
            HomeGoalAvg = homeHistory.Count > 0 ? homeHistory.Average(m => m.Fthg) : 1.3,
            AwayGoalAvg = awayHistory.Count > 0 ? awayHistory.Average(m => m.Ftag) : 1.0,
            HtHomeGoalAvg = homeHistory.Count > 0 && homeHistory.Any(m => m.Hthg.HasValue)
                ? homeHistory.Where(m => m.Hthg.HasValue).Average(m => m.Hthg!.Value) : 0.5,
            HtAwayGoalAvg = awayHistory.Count > 0 && awayHistory.Any(m => m.Htag.HasValue)
                ? awayHistory.Where(m => m.Htag.HasValue).Average(m => m.Htag!.Value) : 0.4
        };
    }

    private Fixture BuildFixtureEntity(
        ApiFixture apiFixture, int leagueId, int season,
        (FixtureStats? Home, FixtureStats? Away) stats, FixtureOdds? apiOdds, HistoricalMatchData? historicalMatch,
        FixtureAverages avgs)
    {
        return new Fixture
        {
            ApiId = apiFixture.ApiId,
            HomeTeamId = apiFixture.HomeTeamApiId,
            AwayTeamId = apiFixture.AwayTeamApiId,
            LeagueId = leagueId,
            Date = apiFixture.Date,
            Status = "FT",

            // Goals
            HomeGoal = apiFixture.HomeGoals ?? 0,
            AwayGoal = apiFixture.AwayGoals ?? 0,
            HomeGoalAvg = avgs.HomeGoalAvg,
            AwayGoalAvg = avgs.AwayGoalAvg,

            // Half-time
            HtHomeGoal = apiFixture.HomeGoalsHalftime ?? 0,
            HtAwayGoal = apiFixture.AwayGoalsHalftime ?? 0,
            HtHomeGoalAvg = avgs.HtHomeGoalAvg,
            HtAwayGoalAvg = avgs.HtAwayGoalAvg,

            // Stats (Priority: API -> Historical -> 0)
            HomeShots = stats.Home?.TotalShots ?? historicalMatch?.HomeShots ?? 0,
            AwayShots = stats.Away?.TotalShots ?? historicalMatch?.AwayShots ?? 0,
            HomeShotsOnTarget = stats.Home?.ShotsOnGoal ?? historicalMatch?.HomeShotsOnTarget ?? 0,
            AwayShotsOnTarget = stats.Away?.ShotsOnGoal ?? historicalMatch?.AwayShotsOnTarget ?? 0,

            // Possession/Passes
            HomeBallPossession = stats.Home?.BallPossession,
            AwayBallPossession = stats.Away?.BallPossession,
            HomePassesAccurate = stats.Home?.PassesAccurate,
            AwayPassesAccurate = stats.Away?.PassesAccurate,

            // xG
            HomeXg = stats.Home?.ExpectedGoals ?? avgs.HomeGoalAvg,
            AwayXg = stats.Away?.ExpectedGoals ?? avgs.AwayGoalAvg,

            // Odds (Priority: API -> Historical -> null)
            HomeWinOdds = apiOdds?.HomeWin ?? historicalMatch?.HomeWinOdds,
            DrawOdds = apiOdds?.Draw ?? historicalMatch?.DrawOdds,
            AwayWinOdds = apiOdds?.AwayWin ?? historicalMatch?.AwayWinOdds,
            Over25Odds = apiOdds?.Over25 ?? historicalMatch?.Over25Odds,
            Under25Odds = apiOdds?.Under25 ?? historicalMatch?.Under25Odds,
            BttsYesOdds = apiOdds?.BttsYes,

            // Flags
            IsCurrentSeason = IsCurrentSeason(season),
            IsDerby = DerbyDetector.IsDerby(apiFixture.HomeTeamApiId, apiFixture.AwayTeamApiId),
            CreatedAt = DateTime.UtcNow
        };
    }

    private class FixtureAverages
    {
        public double HomeGoalAvg { get; set; }
        public double AwayGoalAvg { get; set; }
        public double HtHomeGoalAvg { get; set; }
        public double HtAwayGoalAvg { get; set; }
    }
}
