using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.AutomationDataSync;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Service for syncing fixtures from API-Football.
/// </summary>
public class FixtureSyncService(IApiFootballService apiService, 
    IApplicationDbContext dbContext, ILogger<FixtureSyncService> logger)
    : IFixtureSyncService
{
    private static readonly int[] SupportedLeagues = [ 39, 40, 41, 42, 135, 136, 61, 62, 140, 78, 79, 80, 141, 46, 5, 2, 3 ];
    private HashSet<int>? _existingTeamIds;

    /// <summary>
    /// Get current football season (starts in July)
    /// </summary>
    private static int GetCurrentSeason() => DateTimeOffset.UtcNow.Month >= 7 
        ? DateTimeOffset.UtcNow.Year 
        : DateTimeOffset.UtcNow.Year - 1;

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

        for (var i = 0; i < numberOfSeasons; i++)
        {
            var season = currentSeason - i;
            var seasonResult = await SyncAllLeaguesAsync(season, ct);
            result.Created += seasonResult.Created;
            result.Updated += seasonResult.Updated;
            result.LeaguesSynced += seasonResult.LeaguesSynced;
            result.ErrorMessages.AddRange(seasonResult.ErrorMessages);
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
            var targetLeagueId = leagueId;
            
            // DYNAMIC RESOLUTION for National League (if placeholder ID 5 is used)
            if (leagueId == 5)
            {
                var resolvedId = await apiService.GetLeagueIdByNameAsync("National League", "England");
                if (resolvedId.HasValue)
                {
                    logger.LogInformation("National League ID 5 redirected to Resolved ID {ResolvedId}", resolvedId.Value);
                    targetLeagueId = resolvedId.Value;
                }
            }

            try
            {
                var leagueResult = await SyncLeagueFixturesAsync(targetLeagueId, season, ct);
                result.Updated += leagueResult.Updated;
                result.Created += leagueResult.Created;
                result.LeaguesSynced++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync fixtures for league {LeagueId}", leagueId);
                result.ErrorMessages.Add($"League {leagueId}: {ex.Message}");
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
        var targetLeagueId = leagueId;

        // DYNAMIC RESOLUTION for National League (if placeholder ID 5 is used)
        if (leagueId == 5)
        {
            var resolvedId = await apiService.GetLeagueIdByNameAsync("National League", "England");
            if (resolvedId.HasValue)
            {
                logger.LogInformation("National League ID 5 redirected to Resolved ID {ResolvedId}", resolvedId.Value);
                targetLeagueId = resolvedId.Value;
            }
        }

        logger.LogInformation("Syncing fixtures for league {LeagueId} season {Season}", targetLeagueId, season);

        // Define status categories
        var completedStatuses = new[] { "FT", "AET", "PEN", "ABD", "AWD", "WO" };
        var liveStatuses = new[] { "1H", "HT", "2H", "ET", "BT", "P", "LIVE" };
        var cancelledStatuses = new[] { "PST", "CANC", "INT", "SUSP" };

        var apiFixtures = await apiService.GetFixturesAsync(targetLeagueId, season);
        
        // Phase 1: Upcoming fixtures (capture pre-match odds before they expire)
        var upcomingFixtures = apiFixtures
            .Where(f => f.StatusShort == "NS" && f.Date > DateTimeOffset.UtcNow && f.Date <= DateTimeOffset.UtcNow.AddDays(14))
            .ToList();

        // Pre-fetch existing team IDs globally and cache per request to avoid duplicate team errors
        _existingTeamIds ??= await dbContext.Teams
            .Select(t => t.ApiId)
            .ToHashSetAsync(ct);
        
        var existingTeamIdsPhase1 = _existingTeamIds;

        foreach (var apiFixture in upcomingFixtures)
        {
            await EnsureTeamExistsOptimizedAsync(apiFixture.HomeTeamApiId, apiFixture.HomeTeamName, targetLeagueId, existingTeamIdsPhase1, ct);
            await EnsureTeamExistsOptimizedAsync(apiFixture.AwayTeamApiId, apiFixture.AwayTeamName, targetLeagueId, existingTeamIdsPhase1, ct);

            var existingFixture = await dbContext.Fixtures
                .FirstOrDefaultAsync(f => f.ApiId == apiFixture.ApiId, ct);

            if (existingFixture == null)
            {
                // New upcoming fixture - capture odds now
                var fixture = await CreateUpcomingFixtureAsync(apiFixture, targetLeagueId, season);
                if (fixture == null) continue;
                dbContext.Fixtures.Add(fixture);
                result.Created++;
            }
            else if (existingFixture.HomeWinOdds == null)
            {
                // Existing but no odds yet - try to fetch
                await UpdateFixtureOddsAsync(existingFixture, apiFixture.ApiId);
                result.Updated++;
            }
        }

        // Phase 2: Recently active or completed fixtures (last 14 days to catch delayed results/corrections)
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        
        // We include anything that is:
        // 1. Officially finished (FT, AET, PEN, ABD, etc.)
        // 2. Currently Live (1H, 2H, HT, etc.)
        // 3. Postponed/Cancelled (PST, CANC)
        // 4. Any match whose start time is in the past (even if status is still NS, we need to refresh it)
        var recentOrActiveFixtures = apiFixtures
            .Where(f => (f.Date >= cutoff && f.Date <= DateTimeOffset.UtcNow.AddHours(2)) || 
                        completedStatuses.Contains(f.StatusShort) || 
                        liveStatuses.Contains(f.StatusShort) ||
                        cancelledStatuses.Contains(f.StatusShort))
            .ToList();

        // Pre-fetch existing team IDs globally and cache per request to avoid duplicate team errors
        if (_existingTeamIds == null)
        {
            _existingTeamIds = await dbContext.Teams
                .Select(t => t.ApiId)
                .ToHashSetAsync(ct);
        }
        var existingTeamIds = _existingTeamIds;

        foreach (var apiFixture in recentOrActiveFixtures)
        {
            try
            {
                // Ensure teams exist using local set for speed
                await EnsureTeamExistsOptimizedAsync(apiFixture.HomeTeamApiId, apiFixture.HomeTeamName, targetLeagueId, existingTeamIds, ct);
                await EnsureTeamExistsOptimizedAsync(apiFixture.AwayTeamApiId, apiFixture.AwayTeamName, targetLeagueId, existingTeamIds, ct);

                var existingFixture = await dbContext.Fixtures
                    .FirstOrDefaultAsync(f => f.ApiId == apiFixture.ApiId, ct);

                // Check if match is within 7-day odds window
                var isWithinOddsWindow = apiFixture.Date >= DateTimeOffset.UtcNow.AddDays(-7);
                var isVeryRecent = apiFixture.Date >= DateTimeOffset.UtcNow.AddDays(-2); // 48-hour refresh window

                if (existingFixture == null)
                {
                    // Fixture we never captured - create with full enrichment
                    var fixture = await CreateEnrichedFixtureAsync(apiFixture, targetLeagueId, season, ct, fetchOdds: isWithinOddsWindow);
                    if (fixture != null)
                    {
                        dbContext.Fixtures.Add(fixture);
                        result.Created++;
                    }
                }
                else
                {
                    // Existing fixture - decide if it needs an update
                    bool statusChanged = existingFixture.Status != apiFixture.StatusShort;
                    bool scoreChanged = existingFixture.HomeGoal != (apiFixture.HomeGoals ?? 0) || existingFixture.AwayGoal != (apiFixture.AwayGoals ?? 0);
                    
                    // Always update if:
                    // 1. Status or Score changed
                    // 2. It's currently LIVE
                    // 3. it's very recent (within 48h) to ensure we get final stats/odds corrections
                    if (statusChanged || scoreChanged || liveStatuses.Contains(apiFixture.StatusShort) || isVeryRecent)
                    {
                        await UpdateCompletedFixtureAsync(existingFixture, apiFixture, targetLeagueId, ct);
                        result.Updated++;
                    }
                    
                    if (existingFixture.HomeWinOdds == null && isWithinOddsWindow)
                    {
                        // Missing odds, still within window - try to fetch
                        await UpdateFixtureOddsAsync(existingFixture, apiFixture.ApiId);
                        result.Updated++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process recent/active fixture {ApiId} — skipping.", apiFixture.ApiId);
            }

            // Rate limiting
            await Task.Delay(50, ct);
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
                
                CreatedAt = DateTimeOffset.UtcNow
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
            fixture.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Update previously captured fixture with match results (preserve odds)
    /// </summary>
    private async Task UpdateCompletedFixtureAsync(Fixture fixture, ApiFixture apiFixture, int leagueId, CancellationToken ct)
    {
        // Always update score and status — even if stats API fails (rate limits etc.)
        fixture.Status = apiFixture.StatusShort; // preserve AET/PEN/FT accurately
        fixture.HomeGoal = apiFixture.HomeGoals ?? 0;
        fixture.AwayGoal = apiFixture.AwayGoals ?? 0;
        fixture.HtHomeGoal = apiFixture.HomeGoalsHalftime ?? 0;
        fixture.HtAwayGoal = apiFixture.AwayGoalsHalftime ?? 0;
        fixture.UpdatedAt = DateTimeOffset.UtcNow;

        // Calculate averages from historical data (lightweight DB query, always runs)
        var avgs = await CalculateAveragesAsync(
            apiFixture.HomeTeamApiId, apiFixture.AwayTeamApiId, leagueId, apiFixture.Date, ct);
        fixture.HomeGoalAvg = avgs.HomeGoalAvg;
        fixture.AwayGoalAvg = avgs.AwayGoalAvg;
        fixture.HtHomeGoalAvg = avgs.HtHomeGoalAvg;
        fixture.HtAwayGoalAvg = avgs.HtAwayGoalAvg;

        // Fetch detailed stats — if this fails (API rate limits etc.) we still keep the score above
        try
        {
            var stats = await apiService.GetBothTeamStatsAsync(apiFixture.ApiId);
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch stats for fixture {ApiId} — score/status still saved.", apiFixture.ApiId);
        }
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

            // 2. Process data (Domain Logic)
            var averages = await CalculateAveragesAsync(
                apiFixture.HomeTeamApiId, apiFixture.AwayTeamApiId, leagueId, apiFixture.Date, ct);

            // 3. Build Entity
            return BuildFixtureEntity(apiFixture, leagueId, season, stats, apiOdds, averages);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create fixture for API ID {ApiId}", apiFixture.ApiId);
            return null;
        }
    }
    private async Task<FixtureAverages> CalculateAveragesAsync(
        int homeTeamId,
        int awayTeamId,
        int leagueId,
        DateTimeOffset matchDate,
        CancellationToken ct)
    {
        // Get up to 10 recent home matches for the home team
        var homeMatches = await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => f.Status == "FT" && f.LeagueId == leagueId && f.HomeTeamId == homeTeamId && f.Date < matchDate)
            .OrderByDescending(f => f.Date)
            .Take(16)
            .ToListAsync(ct);

        // Get up to 10 recent away matches for the away team
        var awayMatches = await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => f.Status == "FT" && f.LeagueId == leagueId && f.AwayTeamId == awayTeamId && f.Date < matchDate)
            .OrderByDescending(f => f.Date)
            .Take(16)
            .ToListAsync(ct);

        return new FixtureAverages
        {
            HomeGoalAvg = homeMatches.Count > 0 ? Math.Round(homeMatches.Average(m => m.HomeGoal), 2) : 0.0,
            AwayGoalAvg = awayMatches.Count > 0 ? Math.Round(awayMatches.Average(m => m.AwayGoal), 2) : 0.0,
            HtHomeGoalAvg = homeMatches.Count > 0 ? Math.Round(homeMatches.Average(m => m.HtHomeGoal), 2) : 0.0,
            HtAwayGoalAvg = awayMatches.Count > 0 ? Math.Round(awayMatches.Average(m => m.HtAwayGoal), 2) : 0.0
        };
    }

    private static Fixture BuildFixtureEntity(
        ApiFixture apiFixture, int leagueId, int season,
        (FixtureStats? Home, FixtureStats? Away) stats, FixtureOdds? apiOdds,
        FixtureAverages avgs)
    {
        return new Fixture
        {
            ApiId = apiFixture.ApiId,
            HomeTeamId = apiFixture.HomeTeamApiId,
            AwayTeamId = apiFixture.AwayTeamApiId,
            LeagueId = leagueId,
            Date = apiFixture.Date,
            Status = apiFixture.StatusShort,

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

            HomeShots = stats.Home?.TotalShots ?? 0,
            AwayShots = stats.Away?.TotalShots ?? 0,
            HomeShotsOnTarget = stats.Home?.ShotsOnGoal ?? 0,
            AwayShotsOnTarget = stats.Away?.ShotsOnGoal ?? 0,

            // Possession/Passes
            HomeBallPossession = stats.Home?.BallPossession,
            AwayBallPossession = stats.Away?.BallPossession,
            HomePassesAccurate = stats.Home?.PassesAccurate,
            AwayPassesAccurate = stats.Away?.PassesAccurate,

            // xG
            HomeXg = stats.Home?.ExpectedGoals ?? avgs.HomeGoalAvg,
            AwayXg = stats.Away?.ExpectedGoals ?? avgs.AwayGoalAvg,

            HomeWinOdds = apiOdds?.HomeWin,
            DrawOdds = apiOdds?.Draw,
            AwayWinOdds = apiOdds?.AwayWin,
            Over25Odds = apiOdds?.Over25,
            Under25Odds = apiOdds?.Under25,
            BttsYesOdds = apiOdds?.BttsYes,

            // Flags
            IsCurrentSeason = IsCurrentSeason(season),
            IsDerby = DerbyDetector.IsDerby(apiFixture.HomeTeamApiId, apiFixture.AwayTeamApiId),

            CreatedAt = DateTimeOffset.UtcNow
        };
    }





    /// <summary>
    /// Backfills ELO ratings for all historical fixtures in chronological order.
    /// Resets all teams to 1500 before starting.
    /// </summary>
    public async Task<SyncResult> BackfillEloAsync(CancellationToken ct)
    {
        var result = new SyncResult();
        logger.LogInformation("Starting ELO backfill for all historical fixtures...");

        // 1. Reset all teams to 1500
        var teams = await dbContext.Teams.ToListAsync(ct);
        foreach (var team in teams)
        {
            team.Elo = 1500.0;
        }
        await dbContext.SaveChangesAsync(ct);
        var teamMap = teams.ToDictionary(t => t.ApiId, t => t);

        // 2. Load all completed fixtures chronologically
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Status == "FT")
            .OrderBy(f => f.Date)
            .ToListAsync(ct);

        logger.LogInformation("Processing {Count} fixtures for ELO backfill...", fixtures.Count);

        foreach (var fixture in fixtures)
        {
            if (!teamMap.TryGetValue(fixture.HomeTeamId, out var homeTeam) ||
                !teamMap.TryGetValue(fixture.AwayTeamId, out var awayTeam))
            {
                continue;
            }

            // Capture ELO at kickoff
            fixture.HomeElo = homeTeam.Elo;
            fixture.AwayElo = awayTeam.Elo;

            // Calculate change
            var (homeChange, awayChange) = EloRatingService.CalculateEloChange(
                homeTeam.Elo, awayTeam.Elo, fixture.HomeGoal, fixture.AwayGoal);

            // Update team ratings
            homeTeam.Elo += homeChange;
            awayTeam.Elo += awayChange;

            result.Updated++;
        }

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("ELO backfill complete. Updated {Count} fixtures.", result.Updated);
        
        return result;
    }

    private async Task EnsureTeamExistsOptimizedAsync(int teamApiId, string teamName, int leagueId, HashSet<int> existingTeamIds, CancellationToken ct)
    {
        var hasRealName = !string.IsNullOrWhiteSpace(teamName) && !teamName.StartsWith("Team ");

        if (existingTeamIds.Contains(teamApiId))
        {
            // Team exists — but if we have a real name, check if the stored name is a placeholder and fix it
            if (hasRealName)
            {
                var existingTeam = await dbContext.Teams.FirstOrDefaultAsync(t => t.ApiId == teamApiId, ct);
                if (existingTeam != null && (existingTeam.Name.StartsWith("Unknown Team") || existingTeam.Name.StartsWith("Team ")))
                {
                    existingTeam.Name = teamName;
                    logger.LogInformation("Fixed placeholder team name: ApiId={ApiId} → '{Name}'", teamApiId, teamName);
                }
            }
            return;
        }

        // Check Local collection in case TeamSyncService or a previous league sync added it
        if (dbContext.Teams.Local.Any(t => t.ApiId == teamApiId))
        {
            existingTeamIds.Add(teamApiId);
            return;
        }

        dbContext.Teams.Add(new Team
        {
            ApiId = teamApiId,
            Name = hasRealName ? teamName : $"Unknown Team {teamApiId}",
            LeagueId = leagueId,
            Rank = 0,
            Points = 0,
            GoalsFor = 0,
            GoalsAgainst = 0,
            GoalsDiff = 0,
            Played = 0,
            Win = 0,
            Draw = 0,
            Lose = 0,
            Form = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        
        // Add to local set to avoid adding twice in the same batch
        existingTeamIds.Add(teamApiId);
    }
}
