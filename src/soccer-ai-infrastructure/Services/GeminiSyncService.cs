using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

using SoccerAi.Application.Exceptions;

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
        logger.LogInformation("[GeminiSync] Starting batch sync. Current time: {Now}", now);

        var startUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddDays(5);

        logger.LogInformation("[GeminiSync] Window: {Start} to {End}", startUtc, endUtc);

        // 1. Fetch raw fixtures from DB
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc)
            .OrderBy(f => f.Date)
            .ToListAsync(cancellationToken);

        if (fixtures.Count == 0)
        {
            logger.LogWarning("[GeminiSync] Found 0 fixtures in the 5-day window. Check your local DB sync.");
            return;
        }

        logger.LogInformation("[GeminiSync] Found {Count} total fixtures in window.", fixtures.Count);

        var totalProcessed = 0;
        var toAnalyze = new List<GeminiBatchItem>();
        var skippedCount = 0;

        // 2. Filter BEFORE heavy ML prediction
        foreach (var fixture in fixtures)
        {
            var alreadyAnalyzedCount = await dbContext.FixtureAnalyses
                .CountAsync(a => a.FixtureId == fixture.Id && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

            if (alreadyAnalyzedCount >= 2)
            {
                skippedCount++;
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
                logger.LogError(ex, "[GeminiSync] Failed to run ML prediction for fixture {FixtureId}.", fixture.Id);
            }
        }

        logger.LogInformation("[GeminiSync] Skip Summary: {Skipped} already analyzed. {Remaining} matches to process.", skippedCount, toAnalyze.Count);

        if (toAnalyze.Count == 0)
        {
            logger.LogInformation("[GeminiSync] No matches remaining after filter. Sync complete.");
            return;
        }

        logger.LogInformation("[GeminiSync] Prepared {Count} matches for Gemini. Starting batch processing (Chunk of 5)...", toAnalyze.Count);

        // 3. Process incrementally in batches of 5 and SAVE immediately.
        for (var i = 0; i < toAnalyze.Count; i += 5)
        {
            var chunkList = toAnalyze.Skip(i).Take(5).ToList();
            try
            {
                logger.LogInformation("[GeminiSync] Attempting batch {Num} ({Count} matches)...", (i/5)+1, chunkList.Count);
                
                var results = await geminiService.AnalyzeBatchAsync(chunkList);
                
                foreach (var (fixtureId, bilingualResult) in results)
                {
                    logger.LogInformation("[GeminiSync] Ingesting result for Fixture {Id}: {Rec}", fixtureId, bilingualResult.Recommendation);
                    
                    // Find the original analysis to get the math probs
                    var originalAnalysis = toAnalyze.First(x => x.FixtureId == fixtureId);
                    var mathProbs = new WeightedPrediction
                    {
                        HomeProb = originalAnalysis.ModelHomeWin,
                        DrawProb = originalAnalysis.ModelDraw,
                        AwayProb = originalAnalysis.ModelAwayWin,
                        Over25Prob = originalAnalysis.ModelOver25,
                        BTTSProb = originalAnalysis.ModelBTTS
                    };

                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.En, "en", mathProbs, cancellationToken);
                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.De, "de", mathProbs, cancellationToken);
                    totalProcessed++;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("[GeminiSync] Successfully called SaveChangesAsync for batch.");
                
                logger.LogInformation("[GeminiSync] Batch {Num} fully persisted.", (i/5)+1);
                
                // Rate limiting to respect quota
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (GeminiQuotaExceededException qEx)
            {
                logger.LogCritical(qEx, "[GeminiSync] ABORTING SYNC: Gemini quota exceeded for the day.");
                return; // Direct return stops the loop and the service logic.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GeminiSync] Error processing batch starting at index {Idx}", i);
            }
        }

        logger.LogInformation("[GeminiSync] All batches completed. Total matches analyzed and persisted: {Total}", totalProcessed);
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
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.En, "en", analysis.Prediction ?? new WeightedPrediction(), cancellationToken);
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.De, "de", analysis.Prediction ?? new WeightedPrediction(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Successfully synced Gemini analysis for Fixture {FixtureId}.", fixtureId);
        }
        else
        {
            logger.LogWarning("Gemini returned no results for Fixture {FixtureId}.", fixtureId);
        }
    }

    private async Task UpsertAnalysisAsync(int fixtureId, GeminiBilingualResult aiResult, GeminiLanguageBlock block, string lang, WeightedPrediction math, CancellationToken ct)
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
            
            // MATH CACHE
            existing.HomeProb            = math.HomeProb;
            existing.DrawProb            = math.DrawProb;
            existing.AwayProb            = math.AwayProb;
            existing.Over25Prob          = math.Over25Prob;
            existing.BttsProb            = math.BTTSProb;
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
                
                // MATH CACHE
                HomeProb            = math.HomeProb,
                DrawProb            = math.DrawProb,
                AwayProb            = math.AwayProb,
                Over25Prob          = math.Over25Prob,
                BttsProb            = math.BTTSProb,
                
                CreatedAt           = DateTimeOffset.UtcNow
            });
        }
    }
}
