using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

using SoccerAi.Application.Exceptions;

namespace SoccerAi.Infrastructure.Services;

public class AiSyncService(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    IAiAnalysisService aiService,
    IAiDecisionLayerService aiDecisionLayer,
    ILogger<AiSyncService> logger)
    : IAiSyncService
{
    public async Task SyncUpcomingFixturesAsync(DateTime now, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[AiSync] Starting batch sync. Current time: {Now}", now);

        var startUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-3);
        var endUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(4); // Includes up to 3 days in future (exclusive end)

        logger.LogInformation("[AiSync] Window: {Start} to {End}", startUtc, endUtc);

        // 1. Fetch raw fixtures from DB
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc)
            .OrderBy(f => f.Date)
            .ToListAsync(cancellationToken);

        if (fixtures.Count == 0)
        {
            logger.LogWarning("[AiSync] Found 0 fixtures in the 5-day window. Check your local DB sync.");
            return;
        }

        logger.LogInformation("[AiSync] Found {Count} total fixtures in window.", fixtures.Count);

        var totalProcessed = 0;
        var toAnalyze = new List<AiBatchItem>();
        var skippedCount = 0;

        // 2. Filter BEFORE heavy ML prediction
        foreach (var fixture in fixtures)
        {
            var alreadyAnalyzedCount = await dbContext.FixtureAnalyses
                .CountAsync(a => a.FixtureId == fixture.Id && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

            if (!force && alreadyAnalyzedCount >= 2)
            {
                skippedCount++;
                continue;
            }

            logger.LogWarning("[AiSync] NOT skipping Fixture {FixtureId} (DB PK={DbId}, ApiId={ApiId}): only {Count}/2 analyses found. Date={Date}, Status={Status}",
                fixture.Id, fixture.Id, fixture.ApiId, alreadyAnalyzedCount, fixture.Date, fixture.Status);

            try
            {
                // Run predictive ML for this unanalyzed fixture
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", cancellationToken);

                toAnalyze.Add(new AiBatchItem
                {
                    FixtureId = analysis.FixtureId,
                    League = analysis.LeagueName,
                    HomeTeam = analysis.TeamStats.Home.Name,
                    AwayTeam = analysis.TeamStats.Away.Name,
                    HomeStats = analysis.TeamStats.Home,
                    AwayStats = analysis.TeamStats.Away,
                    HomeGoalAvg = analysis.TeamStats.Home.AvgGoalsScoredLast7,
                    AwayGoalAvg = analysis.TeamStats.Away.AvgGoalsScoredLast7,
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
                });

                if (analysis.OddsHomeWin == 0 && analysis.OddsAwayWin == 0)
                {
                    logger.LogWarning("[AiSync] Fixture {FixtureId} has ZERO odds. AI analysis might be less accurate.", fixture.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AiSync] Failed to run ML prediction for fixture {FixtureId}.", fixture.Id);
            }
        }

        logger.LogInformation("[AiSync] Skip Summary: {Skipped} already analyzed. {Remaining} matches to process.", skippedCount, toAnalyze.Count);

        if (toAnalyze.Count == 0)
        {
            logger.LogInformation("[AiSync] No matches remaining after filter. Sync complete.");
            return;
        }

        logger.LogInformation("[AiSync] Prepared {Count} matches for AI. Starting batch processing (Chunk of 5)...", toAnalyze.Count);

        // 3. Process incrementally in batches of 5 and SAVE immediately.
        for (var i = 0; i < toAnalyze.Count; i += 5)
        {
            var chunkList = toAnalyze.Skip(i).Take(5).ToList();
            try
            {
                logger.LogInformation("[AiSync] Attempting batch {Num} ({Count} matches)...", (i / 5) + 1, chunkList.Count);
                
                var results = await aiService.AnalyzeBatchAsync(chunkList);
                
                foreach (var (fixtureId, bilingualResult) in results)
                {
                    logger.LogInformation("[AiSync] Ingesting result for Fixture {Id}: {Rec}", fixtureId, bilingualResult.Recommendation);
                    
                    // Find the original analysis to get the math probs
                    var originalAnalysis = toAnalyze.FirstOrDefault(x => x.FixtureId == fixtureId);
                    if (originalAnalysis == null)
                    {
                        logger.LogWarning("[AiSync] CRITICAL: AI returned FixtureId {Id} which was NOT in the source batch! Skipping.", fixtureId);
                        continue;
                    }

                    var mathProbs = new WeightedPrediction
                    {
                        HomeProb = originalAnalysis.ModelHomeWin,
                        Over25Prob = originalAnalysis.ModelOver25,
                        BTTSProb = originalAnalysis.ModelBTTS,
                        DrawProb = 0.0, // Explicitly excluded
                        AwayProb = originalAnalysis.ModelAwayWin,
                    };

                    // Call AI Decision Layer for per-market decisions
                    AiFullDecisionResult? decisions = null;
                    try
                    {
                        var decisionPayload = BuildDecisionPayload(originalAnalysis, mathProbs);
                        decisions = await aiDecisionLayer.EvaluateMatchAsync(decisionPayload);
                        if (decisions != null)
                        {
                            logger.LogInformation("[AiSync] Decision Layer for Fixture {Id}: BestBet={Best}, Confidence={Conf}%",
                                fixtureId, decisions.BestBet, decisions.OverallConfidence);
                        }
                    }
                    catch (Exception dlEx)
                    {
                        logger.LogWarning(dlEx, "[AiSync] Decision Layer failed for Fixture {Id}. Persisting without decisions.", fixtureId);
                    }

                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.En, "en", mathProbs, decisions, cancellationToken);
                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.De, "de", mathProbs, decisions, cancellationToken);
                    totalProcessed++;

                    // Small delay between decision layer calls to respect rate limits
                    await Task.Delay(500, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("[AiSync] Successfully called SaveChangesAsync for batch.");
                
                logger.LogInformation("[AiSync] Batch {Num} fully persisted.", (i / 5) + 1);
                
                // Rate limiting to respect quota
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception qEx) when (qEx.Message.Contains("quota"))
            {
                logger.LogCritical(qEx, "[AiSync] ABORTING SYNC: Quota exceeded.");
                return; 
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AiSync] Error processing batch starting at index {Idx}", i);
            }
        }

        logger.LogInformation("All batches completed. Total matches analyzed and persisted: {Total}", totalProcessed);
    }

    public async Task SyncSingleFixtureAsync(int fixtureId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Running targeted AI sync for Fixture {FixtureId}.", fixtureId);

        var langCount = await dbContext.FixtureAnalyses
            .CountAsync(a => a.FixtureId == fixtureId && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

        if (!force && langCount >= 2)
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
        var item = new AiBatchItem
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
            OddsBTTS = analysis.OddsBttsYes
        };

        var results = await aiService.AnalyzeBatchAsync([item]);
        if (results.TryGetValue(fixtureId, out var bilingualResult))
        {
            // Single fixture sync — also call decision layer
            AiFullDecisionResult? decisions = null;
            try
            {
                var decisionPayload = BuildDecisionPayload(item, analysis.Prediction ?? new WeightedPrediction());
                decisions = await aiDecisionLayer.EvaluateMatchAsync(decisionPayload);
            }
            catch (Exception dlEx)
            {
                logger.LogWarning(dlEx, "[AiSync] Decision Layer failed for single fixture {Id}.", fixture.Id);
            }

            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.En, "en", analysis.Prediction ?? new WeightedPrediction(), decisions, cancellationToken);
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.De, "de", analysis.Prediction ?? new WeightedPrediction(), decisions, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Successfully synced AI analysis for Fixture {FixtureId}.", fixtureId);
        }
        else
        {
            logger.LogWarning("AI service returned no results for Fixture {FixtureId}.", fixtureId);
        }
    }

    /// <summary>
    /// Builds enriched JSON payload for the AI Decision Layer from sync batch data.
    /// </summary>
    private static string BuildDecisionPayload(AiBatchItem item, WeightedPrediction math)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            match = new { league = item.League },
            home_team = new
            {
                name = item.HomeTeam,
                rank = item.HomeStats.Rank,
                points = item.HomeStats.Points,
                played = item.HomeStats.Played,
                form = item.HomeStats.Form,
                form_percentage = item.HomeStats.FormPercentage,
                possession = item.HomeStats.Possession,
                momentum = item.HomeStats.Momentum,
                avg_goals_scored_last_3 = item.HomeStats.AvgGoalsScoredLast3,
                avg_goals_conceded_last_3 = item.HomeStats.AvgGoalsConcededLast3,
                avg_goals_scored_last_7 = item.HomeStats.AvgGoalsScoredLast7,
                avg_goals_conceded_last_7 = item.HomeStats.AvgGoalsConcededLast7,
                attack_strength = item.HomeStats.AttackStrength,
                defensive_strength = item.HomeStats.DefensiveStrength,
                clean_sheet_rate = item.HomeStats.CleanSheetRate,
                win_rate = item.HomeStats.WinRate,
                btts_rate_last_3 = item.HomeStats.BTTSRateLast3,
                over25_rate_last_3 = item.HomeStats.Over25RateLast3
            },
            away_team = new
            {
                name = item.AwayTeam,
                rank = item.AwayStats.Rank,
                points = item.AwayStats.Points,
                played = item.AwayStats.Played,
                form = item.AwayStats.Form,
                form_percentage = item.AwayStats.FormPercentage,
                possession = item.AwayStats.Possession,
                momentum = item.AwayStats.Momentum,
                avg_goals_scored_last_3 = item.AwayStats.AvgGoalsScoredLast3,
                avg_goals_conceded_last_3 = item.AwayStats.AvgGoalsConcededLast3,
                avg_goals_scored_last_7 = item.AwayStats.AvgGoalsScoredLast7,
                avg_goals_conceded_last_7 = item.AwayStats.AvgGoalsConcededLast7,
                attack_strength = item.AwayStats.AttackStrength,
                defensive_strength = item.AwayStats.DefensiveStrength,
                clean_sheet_rate = item.AwayStats.CleanSheetRate,
                win_rate = item.AwayStats.WinRate,
                btts_rate_last_3 = item.AwayStats.BTTSRateLast3,
                over25_rate_last_3 = item.AwayStats.Over25RateLast3
            },
            model_probabilities = new
            {
                home_win = math.HomeProb,
                away_win = math.AwayProb,
                over25 = math.Over25Prob,
                btts = math.BTTSProb,
                confidence = math.Confidence
            },
            odds = new
            {
                home = item.OddsHomeWin,
                draw = item.OddsDraw,
                away = item.OddsAwayWin,
                over25 = item.OddsOver25,
                btts = item.OddsBTTS
            }
        });
    }

    private async Task UpsertAnalysisAsync(int fixtureId, AiBilingualResult aiResult, AiLanguageBlock block, string lang, WeightedPrediction math, AiFullDecisionResult? decisions, CancellationToken ct)
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
            existing.DrawProb            = 0.0; // Explicitly excluded
            existing.AwayProb            = math.AwayProb;
            existing.Over25Prob          = math.Over25Prob;
            existing.BttsProb            = math.BTTSProb;
            
            // AI Decision Layer
            if (decisions != null)
            {
                existing.AiOver25Qualified    = decisions.Over25.Qualified;
                existing.AiBttsQualified      = decisions.Btts.Qualified;
                existing.AiUnder25Qualified   = decisions.Under25.Qualified;
                existing.AiGoals23Qualified   = decisions.Goals23.Qualified;
                existing.AiHomeWinQualified   = decisions.HomeWin.Qualified;
                existing.AiAwayWinQualified   = decisions.AwayWin.Qualified;
                existing.AiBestBet            = decisions.BestBet;
                existing.AiOverallConfidence  = decisions.OverallConfidence;
            }
            
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var entity = new FixtureAnalysis
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
                DrawProb            = 0.0, // Explicitly excluded
                AwayProb            = math.AwayProb,
                Over25Prob          = math.Over25Prob,
                BttsProb            = math.BTTSProb,
                
                CreatedAt           = DateTimeOffset.UtcNow
            };
            
            // AI Decision Layer
            if (decisions != null)
            {
                entity.AiOver25Qualified    = decisions.Over25.Qualified;
                entity.AiBttsQualified      = decisions.Btts.Qualified;
                entity.AiUnder25Qualified   = decisions.Under25.Qualified;
                entity.AiGoals23Qualified   = decisions.Goals23.Qualified;
                entity.AiHomeWinQualified   = decisions.HomeWin.Qualified;
                entity.AiAwayWinQualified   = decisions.AwayWin.Qualified;
                entity.AiBestBet            = decisions.BestBet;
                entity.AiOverallConfidence  = decisions.OverallConfidence;
            }
            
            dbContext.FixtureAnalyses.Add(entity);
        }
    }
}
