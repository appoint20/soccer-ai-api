using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Single orchestrated nightly background service.
///
/// Schedule (UTC+1 / local server time):
///   04:00  — Team standings sync (all leagues)
///   04:15  — Fixture sync (all leagues, upcoming + results)
///   04:45  — ML model retraining (python train_models.py)
///   05:00  — Gemini AI analysis batch (today + tomorrow fixtures)
/// </summary>
public class NightlySyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<NightlySyncBackgroundService> logger) : BackgroundService
{
    // ─── Schedule (local time) ───────────────────────────────────────────────
    private static readonly TimeSpan StandingsTime  = new(04, 00, 00); // 04:00 AM
    private static readonly TimeSpan FixturesTime   = new(04, 15, 00); // 04:15 AM
    private static readonly TimeSpan MlTrainTime    = new(04, 45, 00); // 04:45 AM
    private static readonly TimeSpan GeminiTime     = new(05, 00, 00); // 05:00 AM
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "NightlySyncBackgroundService started. Schedule: Standings@04:00, Fixtures@04:15, ML@04:45, Gemini@05:00");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;

                // ── 1: Wait until 04:00 standings sync ──────────────────────
                var standingsNext = NextRunTime(now, StandingsTime);
                logger.LogInformation("Next sync cycle starts with standings at {Time}", standingsNext);
                await Task.Delay(standingsNext - now, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await RunTeamStandingsSyncAsync(stoppingToken);

                // ── 2: Wait until 04:15 fixture sync ────────────────────────
                var fixturesNext = NextRunTime(DateTime.Now, FixturesTime);
                logger.LogInformation("Next step: fixture sync at {Time}", fixturesNext);
                var fixturesDelay = fixturesNext - DateTime.Now;
                if (fixturesDelay > TimeSpan.Zero)
                    await Task.Delay(fixturesDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await RunFixtureSyncAsync(stoppingToken);

                // ── 3: Wait until 04:45 ML training ─────────────────────────
                var mlNext = NextRunTime(DateTime.Now, MlTrainTime);
                logger.LogInformation("Next step: ML training at {Time}", mlNext);
                var mlDelay = mlNext - DateTime.Now;
                if (mlDelay > TimeSpan.Zero)
                    await Task.Delay(mlDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await RunMlTrainingAsync(stoppingToken);

                // ── 4: Wait until 05:00 Gemini sync ─────────────────────────
                var geminiNext = NextRunTime(DateTime.Now, GeminiTime);
                logger.LogInformation("Next step: Gemini sync at {Time}", geminiNext);
                var geminiDelay = geminiNext - DateTime.Now;
                if (geminiDelay > TimeSpan.Zero)
                    await Task.Delay(geminiDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await RunGeminiSyncAsync(stoppingToken);

                logger.LogInformation("Nightly sync cycle complete. Sleeping until next 04:00.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("NightlySyncBackgroundService stopping.");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in nightly sync. Retrying in 1 hour.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    // ─── Step 1: Team standings ──────────────────────────────────────────────
    private async Task RunTeamStandingsSyncAsync(CancellationToken ct)
    {
        logger.LogInformation("[04:00] Starting team standings sync...");
        try
        {
            using var scope = serviceProvider.CreateScope();
            var teamService = scope.ServiceProvider.GetRequiredService<TeamSyncService>();
            var season = CurrentSeason();
            var result = await teamService.SyncAllLeaguesAsync(season, ct);
            logger.LogInformation(
                "[04:00] Standings sync done: {Leagues} leagues, {Created} created, {Updated} updated, {Errors} errors",
                result.LeaguesSynced, result.Created, result.Updated, result.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[04:00] Error during team standings sync");
        }
    }

    // ─── Step 2: Fixtures ────────────────────────────────────────────────────
    private async Task RunFixtureSyncAsync(CancellationToken ct)
    {
        logger.LogInformation("[04:15] Starting fixture sync...");
        try
        {
            using var scope = serviceProvider.CreateScope();
            var fixtureService = scope.ServiceProvider.GetRequiredService<FixtureSyncService>();
            var season = CurrentSeason();
            var result = await fixtureService.SyncAllLeaguesAsync(season, ct);
            logger.LogInformation(
                "[04:15] Fixture sync done: {Leagues} leagues, {Created} created, {Updated} updated, {Errors} errors",
                result.LeaguesSynced, result.Created, result.Updated, result.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[04:15] Error during fixture sync");
        }
    }

    // ─── Step 3: ML training ─────────────────────────────────────────────────
    private async Task RunMlTrainingAsync(CancellationToken ct)
    {
        logger.LogInformation("[04:45] Starting ML model retraining...");
        try
        {
            // Locate train script relative to the app
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "ml", "train_models.py");
            if (!File.Exists(scriptPath))
            {
                // Try repo-relative path (for local dev)
                scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "ml", "train_models.py"));
            }

            if (!File.Exists(scriptPath))
            {
                logger.LogWarning("[04:45] ML training script not found at {Path}. Skipping.", scriptPath);
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
                logger.LogInformation("[04:45] ML training completed successfully.\n{Output}", stdout[..Math.Min(500, stdout.Length)]);
            else
                logger.LogError("[04:45] ML training failed (exit {Code}).\nStdErr: {Err}", process.ExitCode, stderr[..Math.Min(500, stderr.Length)]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[04:45] Error during ML training");
        }
    }

    // ─── Step 4: Gemini AI sync ──────────────────────────────────────────────
    private async Task RunGeminiSyncAsync(CancellationToken ct)
    {
        logger.LogInformation("[05:00] Starting Gemini AI analysis sync...");
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
            var geminiService = scope.ServiceProvider.GetRequiredService<IGeminiAnalysisService>();

            var today = DateTime.Now.Date;
            var endOfTomorrow = today.AddDays(2);

            // Only process fixtures that haven't been analysed yet
            var fixtures = await dbContext.Fixtures
                .Where(f => f.Date >= today && f.Date < endOfTomorrow && f.GeminiRecommendation == null)
                .ToListAsync(ct);

            if (fixtures.Count == 0)
            {
                logger.LogInformation("[05:00] No fixtures require Gemini processing today.");
                return;
            }

            var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
            var teams = await dbContext.Teams
                .Where(t => teamIds.Contains(t.ApiId))
                .ToDictionaryAsync(t => t.ApiId, t => t, ct);

            var geminiBatch = new List<GeminiBatchItem>();
            foreach (var fixture in fixtures)
            {
                var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
                var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
                if (homeTeam == null || awayTeam == null) continue;

                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, ct);
                geminiBatch.Add(new GeminiBatchItem
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

            var chunks = geminiBatch.Chunk(10).ToList();
            logger.LogInformation("[05:00] {Count} matches to analyse, {Chunks} chunks.", geminiBatch.Count, chunks.Count);

            int chunkIndex = 0;
            foreach (var chunk in chunks)
            {
                logger.LogInformation("[05:00] Processing chunk {N}/{Total}...", chunkIndex + 1, chunks.Count);
                var geminiResults = await geminiService.AnalyzeBatchAsync(chunk.ToList());

                bool updated = false;
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
                    updated = true;
                }

                if (updated)
                    await dbContext.SaveChangesAsync(ct);

                chunkIndex++;
                if (chunkIndex < chunks.Count)
                {
                    logger.LogInformation("[05:00] Waiting 5 min before next chunk...");
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
            }

            logger.LogInformation("[05:00] Gemini analysis sync complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[05:00] Error during Gemini sync");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns the next wall-clock UTC+local occurrence of <paramref name="timeOfDay"/>.</summary>
    private static DateTime NextRunTime(DateTime now, TimeSpan timeOfDay)
    {
        var candidate = now.Date.Add(timeOfDay);
        return candidate > now ? candidate : candidate.AddDays(1);
    }

    private static int CurrentSeason() => DateTime.Now.Month >= 7 ? DateTime.Now.Year : DateTime.Now.Year - 1;
}
