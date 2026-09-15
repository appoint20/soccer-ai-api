using System.Globalization;
using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using SoccerAi.Api.Automation;
using SoccerAi.Api.Security;
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
    /// Runs the full sync pipeline: history backfill, standings, fixtures and
    /// odds, analysis precompute, pick settle/publish, and model forecasts.
    /// </summary>
    /// <remarks>
    /// Returns 202 immediately and runs in the background — a full run takes
    /// minutes, far longer than any sensible HTTP timeout, so holding the
    /// request open would just fail the caller while the work continued.
    /// Poll <c>GET /api/automation/sync-status</c> for progress.
    ///
    /// This exists so a scheduler outside the process can drive a sync. The
    /// background worker runs the same pipeline on a timer; when that worker is
    /// unavailable — shut down, sleeping, or not deployed — this is the same
    /// work through a service that stays up. Unlike <c>sync-daily</c>, which
    /// runs an older and shorter sequence, this is the identical pipeline.
    ///
    /// Concurrent calls are rejected with 409 rather than queued: two runs
    /// against one database would double every API call and interleave writes.
    /// </remarks>
    /// <param name="pipeline">The shared pipeline.</param>
    /// <param name="gate">Serializes background automation within this API process.</param>
    /// <param name="resume">Resume an interrupted run from its last completed step.</param>
    [HttpPost("sync-pipeline")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult RunSyncPipeline(
        [FromServices] SoccerAi.Application.Services.Sync.SyncPipeline pipeline,
        [FromServices] ManualAutomationGate gate,
        [FromQuery] bool resume = true)
    {
        if (!gate.TryEnter())
        {
            return Conflict(ApiResponse<object>.Fail(
                "A background automation job is already running. Poll its status URL before starting another run."));
        }

        // ApplicationStopping, not the request token: the run must outlive the
        // response, and only a host shutdown should cancel it.
        var ct = lifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            try
            {
                var completed = await pipeline.RunAsync(resume, ct);
                logger.LogInformation("[AutomationSync] Triggered pipeline run finished (completed: {Completed})", completed);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("[AutomationSync] Triggered pipeline run cancelled by shutdown");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AutomationSync] Triggered pipeline run failed");
            }
            finally
            {
                gate.Exit();
            }
        }, CancellationToken.None);

        return Accepted(ApiResponse<object>.Ok(new
        {
            message = "Sync pipeline started.",
            poll = "/api/automation/sync-status",
            started_at = DateTime.UtcNow,
        }));
    }

    /// <summary>
    /// Generates AI match analysis for every upcoming fixture on one date.
    /// </summary>
    /// <remarks>
    /// Returns 202 at once and runs in the background: narratives are written
    /// one fixture per model request, and a full matchday takes far longer than
    /// any HTTP timeout. Poll the returned <c>poll</c> URL for the report.
    ///
    /// The date is a UTC calendar day — the same day <c>GET /api/analyze</c>
    /// returns — so it targets the matches the app lists. Only fixtures that
    /// have not kicked off are narrated, since the analysis is a pre-match
    /// forecast; the rest of the day is counted in the report, not dropped.
    ///
    /// Without <c>force</c>, fixtures that already have English and German text
    /// are skipped. With it their text is regenerated, and each is a paid call.
    ///
    /// Requires the admin API key (<c>X-API-Key</c>), not just a signed-in user.
    /// The controller's policy also accepts app users' tokens, and every call
    /// here can spend money — with <c>force</c>, again and again.
    /// </remarks>
    /// <param name="jobs">The job runner.</param>
    /// <param name="date">UTC date, <c>yyyy-MM-dd</c>; today or later.</param>
    /// <param name="force">Regenerate narratives that already exist.</param>
    [HttpPost("ai-analysis")]
    [Authorize(Policy = AdminApiKeyAuthenticationDefaults.PolicyName)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult RunAiAnalysisForDate(
        [FromServices] AiAnalysisJobs jobs,
        [FromQuery] string? date,
        [FromQuery] bool force = false)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return BadRequest(ApiResponse<object>.Fail("date is required, formatted yyyy-MM-dd (UTC)."));

        if (day < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(ApiResponse<object>.Fail(
                "date is in the past. AI analysis is a pre-match forecast, and every fixture on that date has kicked off."));

        var job = jobs.TryStart(day, force);
        if (job is null)
        {
            var running = jobs.Current;
            return Conflict(ApiResponse<object>.Fail(running is null
                ? "A background automation job is already running. Poll its status URL before starting another run."
                : $"An AI analysis job for {running.Date:yyyy-MM-dd} is already running. Poll /api/automation/ai-analysis/jobs/{running.Id}"));
        }

        var poll = $"/api/automation/ai-analysis/jobs/{job.Id}";
        logger.LogInformation("[AutomationAi] Started AI analysis job {JobId} for {Date:yyyy-MM-dd} (force {Force})",
            job.Id, day, force);

        return Accepted(poll, ApiResponse<object>.Ok(new
        {
            message = $"AI analysis started for {day:yyyy-MM-dd}.",
            job_id = job.Id,
            date = day,
            force,
            poll,
            started_at = job.StartedAtUtc,
        }));
    }

    /// <summary>Status and report of a manual AI analysis job.</summary>
    /// <remarks>
    /// Jobs are held in memory: a restart forgets them, though narratives already
    /// written stay written.
    /// </remarks>
    [HttpGet("ai-analysis/jobs/{jobId:guid}")]
    [Authorize(Policy = AdminApiKeyAuthenticationDefaults.PolicyName)]
    [ProducesResponseType<ApiResponse<AiAnalysisJob>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetAiAnalysisJob(Guid jobId, [FromServices] AiAnalysisJobs jobs) =>
        jobs.Get(jobId) is { } job
            ? Ok(ApiResponse<AiAnalysisJob>.Ok(job))
            : NotFound(ApiResponse<object>.Fail(
                $"No AI analysis job {jobId}. Jobs are kept in memory and cleared on restart."));

    /// <summary>Sync data, refresh odds, predict, analyze with AI, and publish picks for one UTC date.</summary>
    /// <remarks>
    /// Returns 202 and a job-specific polling URL. Supporting league standings
    /// and season history are refreshed, then odds and predictions target the
    /// chosen day. Uses the accepted prediction model, without retraining.
    /// Final decisions and combinations are built after AI. Started fixtures
    /// are excluded from pre-match analysis and no historical ledger is rewritten.
    /// force_ai=true regenerates existing AI opinions (paid provider calls).
    /// Otherwise current AI text is reused and stale explanations refreshed.
    /// The gate covers background automation in this process, not other replicas
    /// or the scheduled worker. Job status is lost on application restart.
    /// </remarks>
    [HttpPost("sync-date")]
    [Authorize(Policy = AdminApiKeyAuthenticationDefaults.PolicyName)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult RunSyncForDate(
        [FromServices] DateSyncJobs jobs,
        [FromQuery] string? date,
        [FromQuery(Name = "force_ai")] bool forceAi = false)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return BadRequest(ApiResponse<object>.Fail("date is required, formatted yyyy-MM-dd (UTC)."));
        if (day < DateOnly.FromDateTime(DateTime.UtcNow) || day == DateOnly.MaxValue)
            return BadRequest(ApiResponse<object>.Fail("Choose today or a future UTC date before 9999-12-31. Past dates cannot receive new pre-match predictions."));
        var job = jobs.TryStart(day, forceAi);
        if (job is null)
        {
            var running = jobs.Current;
            return Conflict(ApiResponse<object>.Fail(running is null
                ? "A background automation job is already running. Poll its status URL before starting another run."
                : $"Date sync is already running. Poll /api/automation/sync-date/jobs/{running.Id}"));
        }
        var poll = $"/api/automation/sync-date/jobs/{job.Id}";
        return Accepted(poll, ApiResponse<object>.Ok(new
        {
            message = $"Full date sync started for {day:yyyy-MM-dd}.",
            job_id = job.Id, date = day, force_ai = forceAi, poll, started_at = job.StartedAtUtc
        }));
    }

    /// <summary>Per-step progress and results of a manual date sync.</summary>
    [HttpGet("sync-date/jobs/{jobId:guid}")]
    [Authorize(Policy = AdminApiKeyAuthenticationDefaults.PolicyName)]
    [ProducesResponseType<ApiResponse<DateSyncJob>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetDateSyncJob(Guid jobId, [FromServices] DateSyncJobs jobs) =>
        jobs.Get(jobId) is { } job
            ? Ok(ApiResponse<DateSyncJob>.Ok(job))
            : NotFound(ApiResponse<object>.Fail("Job not found. Recent job statuses are held in memory and cleared on restart."));

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
