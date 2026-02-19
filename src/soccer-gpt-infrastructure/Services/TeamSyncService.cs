using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Service for syncing team data (standings) from API-Football.
/// Updates Team records.
/// </summary>
public class TeamSyncService(
    IApiFootballService apiService, IApplicationDbContext dbContext, ILogger<TeamSyncService> logger)
{
    // Supported English leagues
    private static readonly int[] SupportedLeagues = [ 39, 40, 41, 42, 61, 62, 78, 79, 135, 136, 140, 141 ];
    
    /// <summary>
    /// Sync standings for all supported leagues for the current season
    /// </summary>
    public async Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken)
    {
        var result = new SyncResult();
        logger.LogInformation("Starting standings sync for season {Season}", season);
        
        foreach (var leagueId in SupportedLeagues)
        {
            try
            {
                var leagueResult = await SyncLeagueStandingsAsync(leagueId, season, cancellationToken);
                result.Updated += leagueResult.Updated;
                result.Created += leagueResult.Created;
                result.LeaguesSynced++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync standings for league {LeagueId}", leagueId);
                result.Errors++;
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
        
        logger.LogInformation("Syncing standings for league {LeagueId} season {Season}", leagueId, season);
        
        var standings = await apiService.GetStandingsAsync(leagueId, season, ct);
        if (standings.Count == 0)
        {
            logger.LogWarning("No standings returned for league {LeagueId}", leagueId);
            return result;
        }

        foreach (var standingData in standings)
        {
            var existingTeam = await dbContext.Teams.FirstOrDefaultAsync(
                t => t.ApiId == standingData.ApiId, cancellationToken: ct);

            if (existingTeam != null)
            {
                existingTeam.LeagueId = standingData.LeagueId;
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
                existingTeam.UpdatedAt = DateTime.UtcNow;
                
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
}

/// <summary>
/// Result of standings sync operation
/// </summary>
public class SyncResult
{
    public int LeaguesSynced { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
}
