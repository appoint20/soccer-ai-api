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
/// sync is older than the configured threshold (default 3h); otherwise it
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
        var schedule = BuildSchedule(opt);
        logger.LogInformation(
            "Sync worker starting. {Count} slots/day, every {Gap} (UTC): {Schedule}",
            schedule.Count, DescribeCadence(schedule),
            string.Join(", ", schedule.Select(t => t.ToString("HH:mm"))));

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
            var delay = TimeUntilNextRun(DateTimeOffset.UtcNow, schedule);
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

    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// The schedule the loop actually runs: a generated cadence when
    /// <see cref="SyncOptions.IntervalMinutes"/> is set, otherwise the
    /// explicitly listed times.
    /// </summary>
    public static List<TimeOnly> BuildSchedule(SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IntervalMinutes <= 0)
            return ParseSchedule(options.ScheduleUtc);

        // A cadence longer than a day cannot produce a daily grid; fall back
        // rather than emitting a single slot and quietly dropping to one run a
        // day, which is the failure ParseSchedule's own fallback guards against.
        if (options.IntervalMinutes >= MinutesPerDay)
            return ParseSchedule(options.ScheduleUtc);

        var anchor = ((options.IntervalAnchorMinute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;

        var times = new List<TimeOnly>();
        for (var minute = anchor; minute < MinutesPerDay; minute += options.IntervalMinutes)
            times.Add(new TimeOnly(minute / 60, minute % 60));

        // Anchoring past the first interval leaves the pre-anchor part of the
        // day uncovered — 23:40 with a 60-minute cadence would otherwise mean
        // one slot a day. Fill backwards from the anchor to cover it.
        for (var minute = anchor - options.IntervalMinutes; minute >= 0; minute -= options.IntervalMinutes)
            times.Add(new TimeOnly(minute / 60, minute % 60));

        times.Sort();
        return times;
    }

    /// <summary>The smallest gap in the schedule, for the startup log line.</summary>
    private static TimeSpan DescribeCadence(List<TimeOnly> schedule)
    {
        if (schedule.Count < 2) return TimeSpan.FromDays(1);

        var smallest = TimeSpan.FromDays(1) - (schedule[^1] - schedule[0]);
        for (var i = 1; i < schedule.Count; i++)
        {
            var gap = schedule[i] - schedule[i - 1];
            if (gap < smallest) smallest = gap;
        }

        return smallest;
    }

    public static List<TimeOnly> ParseSchedule(string[] scheduleUtc)
    {
        var times = new List<TimeOnly>();
        foreach (var entry in scheduleUtc)
        {
            if (TimeOnly.TryParseExact(entry, "HH:mm", out var time))
                times.Add(time);
        }

        if (times.Count == 0)
            times.AddRange(Enumerable.Range(0, 8).Select(i => new TimeOnly(i * 3, 20)));

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
