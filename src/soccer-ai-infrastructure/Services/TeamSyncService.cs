using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Service for syncing team data (standings) from API-Football.
/// Updates Team records.
/// </summary>
public class TeamSyncService(
    IApiFootballService apiService, IApplicationDbContext dbContext, ILogger<TeamSyncService> logger)
    : ITeamSyncService
{
    // Supported English leagues
    private static readonly int[] SupportedLeagues = [ 39, 40, 41, 42, 61, 62, 78, 79, 80, 135, 136, 140, 141, 46, 5, 2, 3 ];

    private static int GetCurrentSeason() => DateTimeOffset.UtcNow.Month >= 7 
        ? DateTimeOffset.UtcNow.Year 
        : DateTimeOffset.UtcNow.Year - 1;
    
    /// <summary>
    /// Sync standings for all supported leagues for the current season
    /// </summary>
    public async Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken)
    {
        var result = new SyncResult();
        logger.LogInformation("Starting standings sync for season {Season}", season);
        
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
                var leagueResult = await SyncLeagueStandingsAsync(targetLeagueId, season, cancellationToken);
                result.Updated += leagueResult.Updated;
                result.Created += leagueResult.Created;
                result.LeaguesSynced++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync standings for league {LeagueId}", leagueId);
                result.ErrorMessages.Add($"League {leagueId}: {ex.Message}");
            }
            
            // Rate limiting - wait between API calls
            await Task.Delay(50, cancellationToken);
        }
        
        logger.LogInformation(
            "Standings sync complete. Leagues: {Leagues}, Created: {Created}, Updated: {Updated}, Errors: {Errors}",
            result.LeaguesSynced, result.Created, result.Updated, result.Errors);
        
        return result;
    }

    /// <summary>
    /// Sync standings for a specific league
    /// </summary>
    public async Task<SyncResult> SyncLeagueStandingsAsync(int leagueId, int season, CancellationToken ct)
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
        
        logger.LogInformation("Syncing standings for league {LeagueId} season {Season}", targetLeagueId, season);
        
        var standings = await apiService.GetStandingsAsync(targetLeagueId, season, ct);
        if (standings.Count == 0)
        {
            logger.LogWarning("No standings returned for league {LeagueId}", leagueId);
            return result;
        }

        foreach (var standingData in standings)
        {
            // Check Local collection first to avoid tracking conflicts within the same request
            var existingTeam = dbContext.Teams.Local.FirstOrDefault(t => t.ApiId == standingData.ApiId)
                               ?? await dbContext.Teams.FirstOrDefaultAsync(t => t.ApiId == standingData.ApiId, cancellationToken: ct);

            if (existingTeam != null)
            {
                existingTeam.LeagueId = standingData.LeagueId;
                existingTeam.ShortName = standingData.ShortName;
                existingTeam.Rank = standingData.Rank;
                existingTeam.Points = standingData.Points;
                existingTeam.GoalsFor = standingData.GoalsFor;
                existingTeam.GoalsAgainst = standingData.GoalsAgainst;
                existingTeam.GoalsDiff = standingData.GoalsDiff;
                existingTeam.Played = standingData.Played;
                existingTeam.Win = standingData.Win;
                existingTeam.Draw = standingData.Draw;
                existingTeam.Lose = standingData.Lose;
                existingTeam.Form = standingData.Form;
                existingTeam.UpdatedAt = DateTimeOffset.UtcNow;
                
                result.Updated++;
            }
            else
            {
                dbContext.Teams.Add(standingData);
                result.Created++;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        result.LeaguesSynced = 1;
        
        logger.LogInformation(
            "League {LeagueId}: Created {Created}, Updated {Updated} teams", leagueId, result.Created, result.Updated);
        
        return result;
    }

    /// <summary>
    /// Sync standings for multiple seasons
    /// </summary>
    public async Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct)
    {
        var result = new SyncResult();
        var currentSeason = GetCurrentSeason();

        logger.LogInformation("Starting multi-season standings sync for {Count} seasons (from {From} to {To})",
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
            "Multi-season standings sync complete. Total Created: {Created}, Updated: {Updated}, Errors: {Errors}",
            result.Created, result.Updated, result.Errors);

        return result;
    }
}
