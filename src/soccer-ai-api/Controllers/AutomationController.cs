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
    /// Standings -> Fixtures -> ML Retraining -> Gemini Analysis.
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
    [AllowAnonymous] // Temp for testing
    public async Task<IActionResult> SyncMlOnly([FromServices] IMlTrainingService mlService)
    {
        await mlService.TrainModelsAsync();
        return Ok(ApiResponse<object>.Ok(new { message = "ML Training Completed" }));
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(ApiResponse<object>.Ok(new { status = "healthy", subsystem = "automation" }));
    }
}
