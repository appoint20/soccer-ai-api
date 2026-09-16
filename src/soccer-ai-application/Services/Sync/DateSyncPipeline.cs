using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Sync;

public sealed record DatePipelineStep(string Name, string State = "pending",
    DateTimeOffset? StartedAtUtc = null, DateTimeOffset? FinishedAtUtc = null,
    object? Report = null, string? Error = null);

public sealed record DatePredictionReport(int Candidates, int Recomputed, int Skipped,
    IReadOnlyList<int> FailedFixtureIds, IReadOnlyList<string> ModelVersions);

public sealed record DatePublishedBoard(DailyPickBoard Board, int NewlyRecordedTickets);

/// <summary>
/// The manual day's workflow. Supporting season data may span other dates;
/// odds, inference, AI and publication target this UTC day only. Never trains
/// or promotes a model, and never edits an already-published ledger entry.
/// </summary>
public sealed class DateSyncPipeline(IServiceScopeFactory scopeFactory, ILogger<DateSyncPipeline> logger)
{
    public static IReadOnlyList<string> StepNames { get; } = Array.AsReadOnly(new[]
    {
        "standings", "fixtures", "historical_depth", "odds",
        "ml_predictions", "ai_analysis", "final_decisions", "publish_picks"
    });

    public async Task RunAsync(DateOnly date, bool forceAi, Action<DatePipelineStep> progress, CancellationToken ct)
    {
        if (date < DateOnly.FromDateTime(DateTime.UtcNow) || date == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(date), "Choose today or a future UTC date before 9999-12-31.");
        var season = date.Month >= 7 ? date.Year : date.Year - 1;
        int[] leagues;
        await using (var scope = scopeFactory.CreateAsyncScope())
            leagues = scope.ServiceProvider.GetRequiredService<ILeagueTierService>().GetSyncLeagueIds().Distinct().ToArray();

        await Step("standings", () => SyncLeagues(async (services, league) =>
            await services.GetRequiredService<ITeamSyncService>().SyncLeagueStandingsAsync(league, season, ct)));
        await Step("fixtures", () => SyncLeagues(async (services, league) =>
            await services.GetRequiredService<IDateFixtureSyncService>().SyncLeagueForDateAsync(league, date, ct)));
        await Step("historical_depth", async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IFixtureSyncService>()
                .EnsureHistoricalDepthAsync(season, 10, 2, ct);
        }, r => r.Errors > 0 ? "Some supporting history could not be synced." : null);
        await Step("odds", async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDateFixtureSyncService>().CaptureDateOddsAsync(date, ct);
        });
        await Step("ml_predictions", () => RecomputeDateAsync(date, leagues, ct), PredictionFailure);
        await Step("ai_analysis", async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAiSyncService>().SyncDateAsync(date, forceAi, ct);
        }, r => r.Failed > 0 ? "Some fixtures have no completed AI analysis. Retry this date; final publication was skipped." : null);
        // AI qualification flags are persisted before rebuilding the final
        // audit. Never freeze a model-only board and then add AI afterwards.
        await Step("final_decisions", () => RecomputeDateAsync(date, leagues, ct), PredictionFailure);
        await Step("publish_picks", async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var board = await scope.ServiceProvider.GetRequiredService<IDailyPickService>().GetBoardAsync(date, "en", ct);
            var recorded = await scope.ServiceProvider.GetRequiredService<IPickLedger>().RecordAsync(board, ct);
            return new DatePublishedBoard(board, recorded);
        });

        async Task<SyncResult> SyncLeagues(Func<IServiceProvider, int, Task<SyncResult>> sync)
        {
            var total = new SyncResult();
            foreach (var league in leagues)
            {
                ct.ThrowIfCancellationRequested();
                // A failed EF write must not contaminate the next league or
                // step, as it did in the former worker pipeline.
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await sync(scope.ServiceProvider, league);
                if (result.Errors > 0)
                    throw new InvalidOperationException($"League {league} sync reported {result.Errors} error(s). See server logs.");
                total.Created += result.Created;
                total.Updated += result.Updated;
                total.LeaguesSynced++;
            }
            return total;
        }

        async Task Step<T>(string name, Func<Task<T>> run, Func<T, string?>? failure = null)
        {
            ct.ThrowIfCancellationRequested();
            var step = new DatePipelineStep(name, "running", DateTimeOffset.UtcNow);
            progress(step);
            string? reportedFailure = null;
            try
            {
                var report = await run();
                step = step with { Report = report };
                reportedFailure = failure?.Invoke(report);
                if (reportedFailure is not null) throw new InvalidOperationException(reportedFailure);
                ct.ThrowIfCancellationRequested();
                progress(step with { State = "completed", FinishedAtUtc = DateTimeOffset.UtcNow });
            }
            catch (Exception ex)
            {
                progress(step with { State = ct.IsCancellationRequested ? "cancelled" : "failed",
                    FinishedAtUtc = DateTimeOffset.UtcNow,
                    Error = ct.IsCancellationRequested ? "Cancelled by application shutdown."
                        : reportedFailure ?? $"Step {name} failed. See job error and server logs." });
                logger.LogError(ex, "[DateSync] {Date} step {Step} failed", date, name);
                throw;
            }
        }
    }

    private static string? PredictionFailure(DatePredictionReport report) => report.FailedFixtureIds.Count > 0
        ? "Some fixtures could not be predicted. Retry this date; final publication was skipped." : null;

    private async Task<DatePredictionReport> RecomputeDateAsync(DateOnly date, int[] leagues, CancellationToken ct)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        var now = DateTimeOffset.UtcNow;
        List<int> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().Fixtures.AsNoTracking()
                .Where(f => leagues.Contains(f.LeagueId) && f.Date >= start && f.Date < end && f.Date > now && f.Status == "NS")
                .OrderBy(f => f.Date).Select(f => f.Id).ToListAsync(ct);
        var done = 0;
        var skipped = 0;
        var failed = new List<int>();
        var versions = new HashSet<string>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            now = DateTimeOffset.UtcNow;
            if (!await db.Fixtures.AnyAsync(f => f.Id == id && f.Date >= start && f.Date < end &&
                    f.Date > now && f.Status == "NS", ct))
            {
                skipped++;
                continue;
            }
            try
            {
                var analyses = await scope.ServiceProvider.GetRequiredService<IAnalysisPrecomputeService>()
                    .RecomputeFixtureAsync(id, ct);
                if (!analyses.TryGetValue("en", out var en) || !analyses.ContainsKey("de") ||
                    en.Prediction is null || en.DecisionAudit is null || en.Models?.Poisson.IsValid != true)
                {
                    failed.Add(id);
                    continue;
                }
                versions.Add(en.Models?.ModelVersion ?? "unknown");
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[DateSync] Could not recompute fixture {FixtureId}", id);
                failed.Add(id);
            }
        }
        return new(ids.Count, done, skipped, failed, versions.Order().ToArray());
    }
}
