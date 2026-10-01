using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Forecasts;

namespace SoccerAi.Infrastructure.Services;

public sealed class CombinedPredictionAutomationService(IApplicationDbContext db, IServiceScopeFactory scopes,
    ICombinedPredictionAutomationStore store, ICombinedPredictionAutomationReadiness readiness,
    IOptions<CombinedPredictionAutomationOptions> options, TimeProvider clock,
    ILogger<CombinedPredictionAutomationService> logger) : ICombinedPredictionAutomation
{
    public async Task<CombinedPredictionAutomationReport> RunAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new("disabled");
        if (await readiness.BlockingReasonAsync(ct) is { } reason) return new("blocked", Reason: reason);
        var now = clock.GetUtcNow();
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var nextDay = day.AddDays(1);
        if (await db.CombinedPredictionAutomationAttempts.CountAsync(row => row.StartedAtUtc >= day && row.StartedAtUtc < nextDay, ct)
            >= settings.MaxRefreshesPerDay) return new("daily_limit");
        var earliest = now.AddMinutes(settings.MinimumLeadMinutes);
        var latest = now.AddHours(settings.HorizonHours);
        var fixtures = await db.Fixtures.AsNoTracking().Where(fixture => fixture.Status == "NS" && fixture.Date > earliest && fixture.Date <= latest)
            .OrderBy(fixture => fixture.Date).ThenBy(fixture => fixture.Id).ToListAsync(ct);
        var attempted = 0;
        var completed = 0;
        var partial = 0;
        var failed = 0;
        foreach (var fixture in fixtures)
        {
            ct.ThrowIfCancellationRequested();
            if (attempted >= settings.MaxRefreshesPerRun) break;
            var capture = await db.CombinedPredictionSnapshots.AsNoTracking()
                .Where(row => row.FixtureId == fixture.Id && row.KickoffUtc == fixture.Date)
                .OrderByDescending(row => row.CapturedAtUtc).Select(row => (DateTimeOffset?)row.CapturedAtUtc).FirstOrDefaultAsync(ct);
            var window = CombinedPredictionAutomationSchedule.WindowFor(fixture.Date, clock.GetUtcNow(), capture, settings);
            if (window is null) continue;
            var attempt = new CombinedPredictionAutomationAttempt
            {
                FixtureId = fixture.Id, KickoffUtc = fixture.Date, Window = window, StartedAtUtc = clock.GetUtcNow()
            };
            if (!await store.TryReserveAsync(attempt, settings.MaxRefreshesPerDay, ct)) continue;
            attempted++;
            var status = "failed";
            Guid? snapshotId = null;
            string? error = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(settings.RefreshTimeoutMinutes));
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICombinedPredictionRefreshService>()
                    .RefreshAsync(fixture.Id, refreshNarration: true, timeout.Token);
                snapshotId = result.SnapshotId;
                if (result.Prediction.AllFourSourcesAvailable && result.NarrationRefreshed)
                {
                    status = "completed";
                    completed++;
                }
                else
                {
                    status = "partial";
                    partial++;
                    error = "One or more sources or fresh narration were unavailable. Stored predictions are retained.";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                status = "cancelled";
                error = "Worker shutdown interrupted the refresh. The reserved window remains consumed.";
                throw;
            }
            catch (OperationCanceledException)
            {
                status = "timed_out";
                error = "Scheduled refresh exceeded its time limit. Stored predictions are retained.";
                failed++;
            }
            catch (Exception exception)
            {
                error = "Scheduled refresh failed. Check worker logs; this window will not be retried automatically.";
                failed++;
                logger.LogWarning(exception, "Scheduled combined refresh failed for fixture {FixtureId}", fixture.Id);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await store.FinishAsync(attempt.Id, status, snapshotId, error, clock.GetUtcNow(), cleanup.Token);
            }
            if (status != "completed") break;
        }
        return new(failed + partial > 0 ? "completed_with_warnings" : attempted == 0 ? "idle" : "completed",
            attempted, completed, partial, failed);
    }
}
