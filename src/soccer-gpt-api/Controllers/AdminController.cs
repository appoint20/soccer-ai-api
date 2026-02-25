using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using soccer_gpt_application.Interfaces;
using soccer_gpt_api.Security;

namespace soccer_gpt_api.Controllers;

/// <summary>
/// Protected admin controller for manual sync operations.
/// Requires X-API-Key header to be passed.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AdminApiKeyAuthenticationDefaults.PolicyName)]
public class AdminController(
    ITeamSyncService teamSyncService,
    IFixtureSyncService fixtureSyncService,
    ISyncJobRunner syncJobRunner,
    ILogger<AdminController> logger) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<IActionResult> RunFullSync(CancellationToken ct)
    {
        logger.LogInformation("[AdminSync] Manual sync triggered at {Time}", DateTime.Now);

        var season = DateTime.Now.Month >= 7 ? DateTime.Now.Year : DateTime.Now.Year - 1;
        var results = new Dictionary<string, object>();

        // 1. Team standings
        try
        {
            var teamResult = await teamSyncService.SyncAllLeaguesAsync(season, ct);
            results["standings"] = new { teamResult.LeaguesSynced, teamResult.Created, teamResult.Updated, teamResult.Errors };
            logger.LogInformation("[AdminSync] Standings done.");
        }
        catch (Exception ex)
        {
            results["standings"] = new { error = ex.Message };
            logger.LogError(ex, "[AdminSync] Standings failed.");
        }

        // 2. Fixtures
        try
        {
            var fixtureResult = await fixtureSyncService.SyncAllLeaguesAsync(season, ct);
            results["fixtures"] = new { fixtureResult.LeaguesSynced, fixtureResult.Created, fixtureResult.Updated, fixtureResult.Errors };
            logger.LogInformation("[AdminSync] Fixtures done.");
        }
        catch (Exception ex)
        {
            results["fixtures"] = new { error = ex.Message };
            logger.LogError(ex, "[AdminSync] Fixtures failed.");
        }

        // 3. Gemini AI sync
        try
        {
            var processed = await syncJobRunner.RunGeminiAsync(ct);
            results["gemini"] = new { processed };
        }
        catch (Exception ex)
        {
            results["gemini"] = new { error = ex.Message };
        }

        return Ok(new
        {
            message = "Manual sync completed",
            timestamp = DateTime.Now,
            results
        });
    }

    [HttpPost("sync-gemini")]
    public async Task<IActionResult> RunGeminiSync(CancellationToken ct)
    {
        logger.LogInformation("[AdminSync] Manual Gemini sync triggered at {Time}", DateTime.Now);

        try
        {
            var processed = await syncJobRunner.RunGeminiAsync(ct);
            return Ok(new { message = $"Gemini sync complete. Processed {processed} fixtures." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminSync] Gemini sync failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
