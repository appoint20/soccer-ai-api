using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class GeminiSyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<GeminiSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan SyncTime = new(04, 00, 00); // 04:00 AM

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Gemini sync background service started. Will sync at {Time} daily", SyncTime);

        // Run immediately on startup for testing if needed
        await RunSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var nextRun = CalculateNextRun(now);
                var delay = nextRun - now;

                logger.LogInformation("Next Gemini sync scheduled for {NextRun} (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                    await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Gemini sync background service stopping");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Gemini sync background service");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting nightly Gemini sync at {Time}", DateTime.Now);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
            var geminiService = scope.ServiceProvider.GetRequiredService<IGeminiAnalysisService>();

            var today = DateTime.Now.Date;
            var endOfTomorrow = today.AddDays(2);

            // Fetch explicitly where GeminiAnalysis is null so we don't re-run
            var fixtures = await dbContext.Fixtures
                .Where(f => f.Date >= today && f.Date < endOfTomorrow && f.GeminiRecommendation == null)
                .ToListAsync(cancellationToken);

            if (fixtures.Count == 0)
            {
                logger.LogInformation("No fixtures require Gemini processing today.");
                return;
            }

            var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
            var teams = await dbContext.Teams
                .Where(t => teamIds.Contains(t.ApiId))
                .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

            var geminiBatch = new List<GeminiBatchItem>();

            foreach (var fixture in fixtures)
            {
                var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
                var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
                if (homeTeam == null || awayTeam == null) continue;

                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
                
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
            logger.LogInformation($"Found {geminiBatch.Count} matches to process. Split into {chunks.Count} chunks of max 10.");

            int chunkIndex = 0;
            foreach (var chunk in chunks)
            {
                logger.LogInformation($"Processing chunk {chunkIndex + 1}/{chunks.Count} ({chunk.Length} matches)...");
                var geminiResults = await geminiService.AnalyzeBatchAsync(chunk.ToList());

                bool updated = false;
                foreach (var (fixtureId, aiRes) in geminiResults)
                {
                    var entity = fixtures.FirstOrDefault(f => f.Id == fixtureId);
                    if (entity != null)
                    {
                        entity.GeminiRecommendation = aiRes.Recommendation;
                        entity.GeminiConfidence = aiRes.Confidence;
                        entity.GeminiReasoning = aiRes.Reasoning;
                        entity.GeminiAnalysis = aiRes.Analysis;
                        entity.GeminiIsTrap = aiRes.IsTrap;
                        entity.UpdatedAt = DateTime.UtcNow;
                        updated = true;
                    }
                }

                if (updated)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Saved AI results for chunk to DB.");
                }

                chunkIndex++;
                
                // If there are more chunks, delay 5 minutes
                if (chunkIndex < chunks.Count)
                {
                    logger.LogInformation("Waiting 5 minutes before next Gemini chunk...");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
            }
            
            logger.LogInformation("Finished nightly Gemini sync.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Gemini sync");
        }
    }

    private static DateTime CalculateNextRun(DateTime now)
    {
        var today = now.Date;
        var todaySyncTime = today.Add(SyncTime);
        return now < todaySyncTime ? todaySyncTime : todaySyncTime.AddDays(1);
    }
}
