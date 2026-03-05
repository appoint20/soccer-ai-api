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
    public async Task SyncUpcomingFixturesAsync(List<FixtureAnalysisResult> fixtures, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Running Gemini bilingual batch sync (EN + DE) for {Count} fixtures.", fixtures.Count);

        var totalProcessed = 0;
        var toAnalyze = new List<GeminiBatchItem>();

        foreach (var analysis in fixtures)
        {
            // 1. Check if both EN and DE analysis already exist in the database
            var langCount = await dbContext.FixtureAnalyses
                .CountAsync(a => a.FixtureId == analysis.FixtureId && (a.Lang == "en" || a.Lang == "de"), cancellationToken);

            if (langCount >= 2)
            {
                logger.LogInformation("Skipping Fixture {FixtureId} as both EN and DE analyses already exist.", analysis.FixtureId);
                continue;
            }

            // 2. Prepare batch item
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

        if (toAnalyze.Count == 0)
        {
            logger.LogInformation("No new fixtures need analysis.");
            return;
        }

        logger.LogInformation("Found {Count} fixtures needing new Gemini analysis.", toAnalyze.Count);

        // Process in batches of 5
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

                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Successfully processed batch of {Count} fixtures.", chunkList.Count);
                
                // Optional rate limiting if needed, but 10 matches per call is efficient
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing batch of Gemini analysis.");
            }
        }

        logger.LogInformation("Gemini sync completed. Total matches analyzed: {Total}", totalProcessed);
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
