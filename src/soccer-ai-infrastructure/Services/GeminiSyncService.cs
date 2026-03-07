using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

public class GeminiSyncService(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    IGeminiAnalysisService geminiService,
    ILogger<GeminiSyncService> logger)
    : IGeminiSyncService
{
    public async Task SyncUpcomingFixturesAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting optimized Gemini batch sync for the next 5 days.");

        var startUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddDays(5);

        // 1. Fetch raw fixtures from DB
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc)
            .OrderBy(f => f.Date)
            .ToListAsync(cancellationToken);

        if (fixtures.Count == 0)
        {
            logger.LogInformation("No fixtures found in the next 5 days.");
            return;
        }

        logger.LogInformation("Found {Count} upcoming fixtures in the database window.", fixtures.Count);

        var totalProcessed = 0;
        var toAnalyze = new List<GeminiBatchItem>();

        // 2. Filter BEFORE heavy ML prediction
        foreach (var fixture in fixtures)
        {
            var alreadyAnalyzedCount = await dbContext.FixtureAnalyses
                .CountAsync(a => a.FixtureId == fixture.Id && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

            if (alreadyAnalyzedCount >= 2)
            {
                logger.LogInformation("Skipping Fixture {FixtureId}: Already analyzed completely.", fixture.Id);
                continue;
            }

            try
            {
                // Run predictive ML for this unanalyzed fixture
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", cancellationToken);

                toAnalyze.Add(new GeminiBatchItem
                {
                    FixtureId = analysis.FixtureId,
                    League = analysis.LeagueName,
                    HomeTeam = analysis.TeamStats.Home.Name,
                    AwayTeam = analysis.TeamStats.Away.Name,
                    HomeStats = analysis.TeamStats.Home,
                    AwayStats = analysis.TeamStats.Away,
                    ModelHomeWin = analysis.Models.Poisson.HomeWin,
                    ModelDraw = analysis.Models.Poisson.Draw,
                    ModelAwayWin = analysis.Models.Poisson.AwayWin,
                    ModelOver25 = analysis.Models.Poisson.Over25,
                    ModelBTTS = analysis.Models.Poisson.BTTS,
                    OddsHomeWin = analysis.OddsHomeWin,
                    OddsDraw = analysis.OddsDraw,
                    OddsAwayWin = analysis.OddsAwayWin,
                    OddsOver25 = analysis.OddsOver25,
                    OddsBTTS = analysis.OddsBttsYes,
                    HomeElo = analysis.HomeElo,
                    AwayElo = analysis.AwayElo
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to predict stats for fixture {FixtureId} before Gemini batching.", fixture.Id);
            }
        }

        if (toAnalyze.Count == 0)
        {
            logger.LogInformation("No new fixtures need Gemini analysis.");
            return;
        }

        logger.LogInformation("Prepared {Count} new matches for Gemini AI logic. Starting batch processing...", toAnalyze.Count);

        // 3. Process incrementally in batches of 5 and SAVE immediately.
        for (var i = 0; i < toAnalyze.Count; i += 5)
        {
            var chunkList = toAnalyze.Skip(i).Take(5).ToList();
            try
            {
                logger.LogInformation("Calling Gemini for batch of {Count} matches...", chunkList.Count);
                
                var results = await geminiService.AnalyzeBatchAsync(chunkList);
                
                foreach (var (fixtureId, bilingualResult) in results)
                {
                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.En, "en", cancellationToken);
                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.De, "de", cancellationToken);
                    totalProcessed++;
                }

                // SAVE INCREMENTALLY: Prevents total data loss on Cloud Run timeout.
                await dbContext.SaveChangesAsync(cancellationToken);
                
                // MIRROR BACK TO GCS: Bypass FUSE SQL Lock errors by uploading the whole file explicitly
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
                {
                    try
                    {
                        File.Copy("/tmp/soccer.db", "/app/data/soccer.db", true);
                        logger.LogInformation("Database successfully mirrored back to GCS FUSE volume.");
                    }
                    catch (Exception ioEx)
                    {
                        logger.LogError(ioEx, "Failed to mirror database back to GCS. Data is safe in /tmp but will be lost if container restarts.");
                    }
                }
                
                logger.LogInformation("Successfully persisted {Count} Gemini results.", chunkList.Count);
                
                // Rate limiting to respect quota
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Gemini batch.");
            }
        }

        logger.LogInformation("Gemini optimized sync completed. Total new matches analyzed: {Total}", totalProcessed);
    }

    public async Task SyncSingleFixtureAsync(int fixtureId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Running targeted Gemini sync for Fixture {FixtureId}.", fixtureId);

        var langCount = await dbContext.FixtureAnalyses
            .CountAsync(a => a.FixtureId == fixtureId && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

        if (langCount >= 2)
        {
            logger.LogInformation("Targeted sync skipped: Fixture {FixtureId} already has both EN and DE analyses.", fixtureId);
            return;
        }

        var fixture = await dbContext.Fixtures.FirstOrDefaultAsync(f => f.Id == fixtureId, cancellationToken);
        if (fixture == null)
        {
            logger.LogWarning("Fixture {FixtureId} not found.", fixtureId);
            return;
        }

        var teams = await dbContext.Teams
            .Where(t => t.ApiId == fixture.HomeTeamId || t.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
        var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
        if (homeTeam == null || awayTeam == null)
        {
            logger.LogWarning("Teams not found for context of fixture {FixtureId}.", fixtureId);
            return;
        }

        var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", cancellationToken);
        var item = new GeminiBatchItem
        {
            FixtureId = fixture.Id,
            League    = analysis.LeagueName,
            HomeTeam  = homeTeam.Name,
            AwayTeam  = awayTeam.Name,
            HomeStats = analysis.TeamStats.Home,
            AwayStats = analysis.TeamStats.Away,
            ModelHomeWin = analysis.Models.Poisson.HomeWin,
            ModelDraw = analysis.Models.Poisson.Draw,
            ModelAwayWin = analysis.Models.Poisson.AwayWin,
            ModelOver25 = analysis.Models.Poisson.Over25,
            ModelBTTS = analysis.Models.Poisson.BTTS,
            OddsHomeWin = analysis.OddsHomeWin,
            OddsDraw = analysis.OddsDraw,
            OddsAwayWin = analysis.OddsAwayWin,
            OddsOver25 = analysis.OddsOver25,
            OddsBTTS = analysis.OddsBttsYes,
            HomeElo = analysis.HomeElo,
            AwayElo = analysis.AwayElo
        };

        var results = await geminiService.AnalyzeBatchAsync([item]);
        if (results.TryGetValue(fixtureId, out var bilingualResult))
        {
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.En, "en", cancellationToken);
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.De, "de", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Successfully synced Gemini analysis for Fixture {FixtureId}.", fixtureId);
        }
        else
        {
            logger.LogWarning("Gemini returned no results for Fixture {FixtureId}.", fixtureId);
        }
    }

    private async Task UpsertAnalysisAsync(int fixtureId, GeminiBilingualResult aiResult, GeminiLanguageBlock block, string lang, CancellationToken ct)
    {
        var existing = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixtureId && a.Lang == lang, ct);

        if (existing != null)
        {
            existing.Lang                = lang;
            existing.Recommendation      = aiResult.Recommendation;
            existing.Confidence          = aiResult.Confidence;
            existing.PredictionReason    = block.PredictionReason ?? "";
            existing.Analysis            = block.Analysis ?? "";
            existing.TrapDetected        = aiResult.TrapDetected;
            existing.TrapReason          = block.TrapReason;
            existing.ConsensusEvaluation = block.ConsensusEvaluation ?? "";
            existing.BttsSummary         = block.Summaries?.Btts ?? "";
            existing.Over25Summary       = block.Summaries?.Over25 ?? "";
            existing.Under25Summary      = block.Summaries?.Under25 ?? "";
            existing.HomeWinSummary      = block.Summaries?.HomeWin ?? "";
            existing.AwayWinSummary      = block.Summaries?.AwayWin ?? "";
        }
        else
        {
            dbContext.FixtureAnalyses.Add(new FixtureAnalysis
            {
                FixtureId           = fixtureId,
                Recommendation      = aiResult.Recommendation,
                Lang                = lang,
                Confidence          = aiResult.Confidence,
                PredictionReason    = block.PredictionReason ?? "",
                Analysis            = block.Analysis ?? "",
                TrapDetected        = aiResult.TrapDetected,
                TrapReason          = block.TrapReason,
                ConsensusEvaluation = block.ConsensusEvaluation ?? "",
                BttsSummary         = block.Summaries?.Btts ?? "",
                Over25Summary       = block.Summaries?.Over25 ?? "",
                Under25Summary      = block.Summaries?.Under25 ?? "",
                HomeWinSummary      = block.Summaries?.HomeWin ?? "",
                AwayWinSummary      = block.Summaries?.AwayWin ?? "",
                CreatedAt           = DateTimeOffset.UtcNow
            });
        }
    }
}
