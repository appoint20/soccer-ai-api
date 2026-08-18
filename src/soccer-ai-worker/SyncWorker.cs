using SoccerAi.Application.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Worker;

/// <summary>
/// Schedules the sync pipeline. ALL scheduling math uses DateTimeOffset.UtcNow
/// (the old DailySyncBackgroundService used DateTime.Now — server-local time bug).
///
/// Startup behavior: syncs immediately ONLY when the persisted last successful
/// sync is older than the configured threshold (default 20h); otherwise it
/// waits for the next scheduled UTC time.
/// </summary>
public sealed class SyncWorker(
    IServiceScopeFactory scopeFactory,
    SyncPipeline pipeline,
    IOptions<SyncOptions> options,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        logger.LogInformation("Sync worker starting. Schedule (UTC): {Schedule}",
            string.Join(", ", opt.ScheduleUtc));

        // ── Startup: sync only if stale (> threshold since last success) ──
        try
        {
            if (await IsSyncOverdueAsync(opt, stoppingToken))
            {
                logger.LogInformation("Last successful sync is older than {Hours}h — running startup sync",
                    opt.StartupSyncThresholdHours);
                await pipeline.RunAsync(resume: true, stoppingToken);
            }
            else
            {
                logger.LogInformation("Recent successful sync found — skipping startup sync");
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup sync check failed");
        }

        // ── Scheduled loop ──
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTimeOffset.UtcNow, ParseSchedule(opt.ScheduleUtc));
            logger.LogInformation("Next sync at {Next:u} (in {Delay})",
                DateTimeOffset.UtcNow + delay, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await pipeline.RunAsync(resume: true, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled sync failed — waiting for next slot");
                // brief cool-down so a hard failure cannot spin the loop
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Sync worker stopping.");
    }

    private async Task<bool> IsSyncOverdueAsync(SyncOptions opt, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var state = await SyncPipeline.GetOrCreateStateAsync(db, ct);

        return state.LastSuccessfulSyncUtc == null ||
               DateTimeOffset.UtcNow - state.LastSuccessfulSyncUtc.Value >
               TimeSpan.FromHours(opt.StartupSyncThresholdHours);
    }

    // ── Pure scheduling math (unit-tested) ────────────────────────────────────

    public static List<TimeOnly> ParseSchedule(string[] scheduleUtc)
    {
        var times = new List<TimeOnly>();
        foreach (var entry in scheduleUtc)
        {
            if (TimeOnly.TryParseExact(entry, "HH:mm", out var time))
                times.Add(time);
        }

        if (times.Count == 0)
            times.Add(new TimeOnly(15, 30)); // safe default, UTC

        times.Sort();
        return times;
    }

    public static TimeSpan TimeUntilNextRun(DateTimeOffset nowUtc, List<TimeOnly> scheduleUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);

        foreach (var time in scheduleUtc)
        {
            var candidate = new DateTimeOffset(today, time, TimeSpan.Zero);
            if (candidate > nowUtc)
                return candidate - nowUtc;
        }

        // All of today's slots have passed → first slot tomorrow.
        var tomorrow = new DateTimeOffset(today.AddDays(1), scheduleUtc[0], TimeSpan.Zero);
        return tomorrow - nowUtc;
    }
}
