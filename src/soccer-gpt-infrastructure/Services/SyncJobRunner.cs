using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class SyncJobRunner(
    ITeamSyncService teamSyncService,
    IFixtureSyncService fixtureSyncService,
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    IGeminiAnalysisService geminiService,
    ILogger<SyncJobRunner> logger) : ISyncJobRunner
{
    public async Task<SyncResult> RunStandingsAsync(int season, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running standings sync for season {Season}", season);
        return await teamSyncService.SyncAllLeaguesAsync(season, cancellationToken);
    }

    public async Task<SyncResult> RunFixturesAsync(int season, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running fixtures sync for season {Season}", season);
        return await fixtureSyncService.SyncAllLeaguesAsync(season, cancellationToken);
    }

    public async Task<int> RunGeminiAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running Gemini batch sync.");

        var today = DateTime.UtcNow.Date;
        var windowEnd = today.AddDays(7); // Increased window to 7 days

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= today && f.Date < windowEnd && f.GeminiRecommendation == null)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Found {Count} candidate fixtures for Gemini processing in window {Start} to {End}", 
            fixtures.Count, today, windowEnd);

        if (fixtures.Count == 0)
        {
            return 0;
        }

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var batch = new List<GeminiBatchItem>();
        foreach (var fixture in fixtures)
        {
            var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
            var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
            if (homeTeam == null || awayTeam == null)
                continue;

            var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
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

        var processed = 0;
        var chunks = batch.Chunk(10).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var results = await geminiService.AnalyzeBatchAsync(chunk.ToList());
            foreach (var (fixtureId, aiResult) in results)
            {
                var entity = fixtures.FirstOrDefault(f => f.Id == fixtureId);
                if (entity == null)
                    continue;

                entity.GeminiRecommendation = aiResult.Recommendation;
                entity.GeminiConfidence = aiResult.Confidence;
                entity.GeminiReasoning = aiResult.Reasoning;
                entity.GeminiAnalysis = aiResult.Analysis;
                entity.GeminiIsTrap = aiResult.IsTrap;
                entity.GeminiTrapReason = aiResult.TrapReason;
                entity.GeminiOneLineSummary = aiResult.OneLineSummary;
                entity.GeminiBttsSummary = aiResult.BttsSummary;
                entity.GeminiOver25Summary = aiResult.Over25Summary;
                entity.GeminiUnder25Summary = aiResult.Under25Summary;
                entity.GeminiHomeWinSummary = aiResult.HomeWinSummary;
                entity.GeminiAwayWinSummary = aiResult.AwayWinSummary;
                entity.UpdatedAt = DateTime.UtcNow;
                processed++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (i < chunks.Count - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }

        logger.LogInformation("Gemini sync completed. Processed {Processed} fixtures.", processed);
        return processed;
    }

    public async Task<bool> RunMlTrainingAsync(CancellationToken cancellationToken)
    {
        var scriptPath = ResolveMlTrainingScriptPath();
        if (scriptPath == null)
        {
            logger.LogWarning("ML training script not found. Skipping ML retraining.");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.LogError("Unable to start python3 process for ML training.");
            return false;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            logger.LogError(
                "ML retraining failed with exit code {Code}. Error: {Error}",
                process.ExitCode,
                TrimForLog(stderr));
            return false;
        }

        logger.LogInformation("ML retraining completed successfully. Output: {Output}", TrimForLog(stdout));
        return true;
    }

    public async Task<SyncResult> RunNightlyAsync(int season, CancellationToken cancellationToken)
    {
        var aggregate = new SyncResult();

        var standings = await RunStandingsAsync(season, cancellationToken);
        aggregate.Created += standings.Created;
        aggregate.Updated += standings.Updated;
        aggregate.Errors += standings.Errors;
        aggregate.LeaguesSynced += standings.LeaguesSynced;

        var fixtures = await RunFixturesAsync(season, cancellationToken);
        aggregate.Created += fixtures.Created;
        aggregate.Updated += fixtures.Updated;
        aggregate.Errors += fixtures.Errors;
        aggregate.LeaguesSynced += fixtures.LeaguesSynced;

        _ = await RunMlTrainingAsync(cancellationToken);
        _ = await RunGeminiAsync(cancellationToken);

        return aggregate;
    }

    private static string? ResolveMlTrainingScriptPath()
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "scripts", "ml", "train_models.py");
        if (File.Exists(runtimePath))
            return runtimePath;

        // local repo fallback
        var localPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "ml", "train_models.py"));
        return File.Exists(localPath) ? localPath : null;
    }

    private static string TrimForLog(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        const int max = 500;
        return input.Length <= max ? input : input[..max];
    }
}
