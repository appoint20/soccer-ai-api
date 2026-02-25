using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_api.Controllers;

/// <summary>
/// Protected admin controller for manual sync operations.
/// Requires X-API-Key header to be passed.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController(
    TeamSyncService teamSyncService,
    FixtureSyncService fixtureSyncService,
    IServiceProvider serviceProvider,
    ILogger<AdminController> logger) : ControllerBase
{
    private const string ApiKey = "admin123";

    [HttpPost("sync")]
    public async Task<IActionResult> RunFullSync(CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("X-API-Key", out var key) || key != ApiKey)
            return Unauthorized(new { error = "Invalid API key" });

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
            using var scope = serviceProvider.CreateScope();
            var geminiService = scope.ServiceProvider.GetRequiredService<soccer_gpt_infrastructure.Services.NightlySyncBackgroundService>();
            // Note: Gemini triggered via the service directly is not feasible without exposing the method;
            // Instead, call the dedicated Gemini background endpoint by invoking it as a separate request.
            results["gemini"] = new { status = "Gemini AI sync will run automatically at 05:00 AM tonight or trigger via /api/admin/sync-gemini" };
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
        if (!Request.Headers.TryGetValue("X-API-Key", out var key) || key != ApiKey)
            return Unauthorized(new { error = "Invalid API key" });

        logger.LogInformation("[AdminSync] Manual Gemini sync triggered at {Time}", DateTime.Now);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<soccer_gpt_application.Interfaces.IApplicationDbContext>();
            var analysisService = scope.ServiceProvider.GetRequiredService<soccer_gpt_application.Interfaces.IMatchAnalysisService>();
            var geminiService = scope.ServiceProvider.GetRequiredService<soccer_gpt_application.Interfaces.IGeminiAnalysisService>();

            var today = DateTime.Now.Date;
            var endOfTomorrow = today.AddDays(2);

            var fixtures = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                dbContext.Fixtures.Where(f => f.Date >= today && f.Date < endOfTomorrow && f.GeminiRecommendation == null), ct);

            if (fixtures.Count == 0)
                return Ok(new { message = "No fixtures require Gemini processing." });

            var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
            var teams = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                dbContext.Teams.Where(t => teamIds.Contains(t.ApiId)), t => t.ApiId, t => t, ct);

            var batch = new List<GeminiBatchItem>();
            foreach (var fixture in fixtures)
            {
                var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
                var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
                if (homeTeam == null || awayTeam == null) continue;

                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, ct);
                batch.Add(new GeminiBatchItem
                {
                    FixtureId = fixture.Id,
                    League = analysis.LeagueName,
                    HomeTeam = homeTeam.Name,
                    AwayTeam = awayTeam.Name,
                    HomeStats = analysis.TeamStats.Home,
                    AwayStats = analysis.TeamStats.Away,
                    Prediction = analysis.Prediction
                });
            }

            int processed = 0;
            foreach (var chunk in batch.Chunk(10))
            {
                var geminiResults = await geminiService.AnalyzeBatchAsync(chunk.ToList());
                foreach (var (fixtureId, aiRes) in geminiResults)
                {
                    var entity = fixtures.FirstOrDefault(f => f.Id == fixtureId);
                    if (entity == null) continue;
                    entity.GeminiRecommendation  = aiRes.Recommendation;
                    entity.GeminiConfidence       = aiRes.Confidence;
                    entity.GeminiReasoning        = aiRes.Reasoning;
                    entity.GeminiAnalysis         = aiRes.Analysis;
                    entity.GeminiIsTrap           = aiRes.IsTrap;
                    entity.GeminiTrapReason       = aiRes.TrapReason;
                    entity.GeminiOneLineSummary   = aiRes.OneLineSummary;
                    entity.GeminiBttsSummary      = aiRes.BttsSummary;
                    entity.GeminiOver25Summary    = aiRes.Over25Summary;
                    entity.GeminiUnder25Summary   = aiRes.Under25Summary;
                    entity.GeminiHomeWinSummary   = aiRes.HomeWinSummary;
                    entity.GeminiAwayWinSummary   = aiRes.AwayWinSummary;
                    entity.UpdatedAt              = DateTime.UtcNow;
                    processed++;
                }
                await dbContext.SaveChangesAsync(ct);
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
            }

            return Ok(new { message = $"Gemini sync complete. Processed {processed} fixtures." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminSync] Gemini sync failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
