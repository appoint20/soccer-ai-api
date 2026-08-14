using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using SoccerAi.Application.Features.Automation;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

/// <summary>
/// Dedicated automation controller for scheduled tasks.
/// Secured via CombinedPolicy (supports API Key and JWT).
/// </summary>
[ApiController]
[Route("api/automation")]
[Authorize(Policy = "CombinedPolicy")]
public class AutomationController(IMediator mediator, IHostApplicationLifetime lifetime, ILogger<AutomationController> logger) : ControllerBase
{
    /// <summary>
    /// Executes the full daily synchronization job:
    /// Standings -> Fixtures -> ML retraining -> AI analysis.
    /// </summary>
    [HttpPost("sync-daily")]
    public async Task<IActionResult> RunDailySync()
    {
        var season = DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        logger.LogInformation("[AutomationSync] Received daily sync request for season {Season}", season);

        try
        {
            // Use ApplicationStopping token instead of the request cancellation token
            // so the sync continues even if the user closes their browser/connection.
            await mediator.SendAsync(new RunDailySyncCommand(season), lifetime.ApplicationStopping);
            return Ok(ApiResponse<object>.Ok(
                new { message = "Daily sync completed successfully", timestamp = DateTime.UtcNow }));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[AutomationSync] Daily sync was gracefully cancelled due to application shutdown.");
            return StatusCode(503, ApiResponse<object>.Fail("Sync aborted: Application is shutting down."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AutomationSync] Daily sync failed");
            return StatusCode(500, ApiResponse<object>.Fail($"Sync failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lightweight health check specifically for the automation subsystem.
    /// </summary>
    [HttpPost("sync-ml-only")]
    public async Task<IActionResult> SyncMlOnly([FromServices] IMlTrainingService mlService)
    {
        await mlService.TrainModelsAsync();
        return Ok(ApiResponse<object>.Ok(new { message = "ML Training Completed" }));
    }

    /// <summary>
    /// Recompute the persisted analysis snapshot for a single fixture.
    /// The sync agent (or an admin) uses this after data for a fixture changes.
    /// </summary>
    [HttpPost("recompute/{fixtureId:int}")]
    public async Task<IActionResult> RecomputeFixture(
        int fixtureId,
        [FromServices] IAnalysisPrecomputeService precomputeService,
        CancellationToken ct)
    {
        var results = await precomputeService.RecomputeFixtureAsync(fixtureId, ct);
        if (results.Count == 0)
            return NotFound(ApiResponse<object>.Fail($"Fixture {fixtureId} not found or could not be recomputed."));

        return Ok(ApiResponse<object>.Ok(new
        {
            message = $"Recomputed analysis snapshot for fixture {fixtureId}",
            languages = results.Keys,
            timestamp = DateTime.UtcNow
        }));
    }

    /// <summary>
    /// Sync fixtures only (past results + upcoming) — skips ML and AI analysis.
    /// Use this to quickly refresh match results and upcoming fixture data.
    /// </summary>
    [HttpPost("sync-fixtures")]
    public async Task<IActionResult> SyncFixtures([FromServices] IFixtureSyncService fixtureSyncService)
    {
        var season = DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        logger.LogInformation("[AutomationSync] Fixture-only sync requested for season {Season}", season);

        try
        {
            var result = await fixtureSyncService.SyncAllLeaguesAsync(season, lifetime.ApplicationStopping);
            return Ok(ApiResponse<object>.Ok(new
            {
                message = "Fixture sync completed",
                season,
                created = result.Created,
                updated = result.Updated,
                leagues_synced = result.LeaguesSynced,
                errors = result.Errors,
                timestamp = DateTime.UtcNow
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AutomationSync] Fixture sync failed");
            return StatusCode(500, ApiResponse<object>.Fail($"Fixture sync failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Liveness only: confirms this process is serving requests.
    /// </summary>
    /// <remarks>
    /// Says nothing about the sync agent, which runs in a different service.
    /// Use <c>GET /api/automation/sync-status</c> to find out whether syncing
    /// actually works.
    /// </remarks>
    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(ApiResponse<object>.Ok(new { status = "healthy", subsystem = "automation" }));
    }

    /// <summary>
    /// Whether the sync agent is actually working: when it last succeeded, what
    /// it last failed on, and how much data is in the database.
    /// </summary>
    /// <param name="query">Optional <c>stale_after_hours</c> threshold.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("sync-status")]
    [ProducesResponseType<ApiResponse<GetSyncStatusResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncStatus(
        [FromQuery] GetSyncStatusQuery query, CancellationToken ct = default)
    {
        var response = await mediator
            .RequestAsync<GetSyncStatusQuery, GetSyncStatusResponse>(query, ct);

        return Ok(ApiResponse<GetSyncStatusResponse>.Ok(response));
    }
}
