using FluentAssertions;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Worker;

namespace soccer_ai_unit_tests.Worker;

public class SyncWorkerScheduleTests
{
    [Fact]
    public void ParseSchedule_ValidTimes_ParsedAndSorted()
    {
        var times = SyncWorker.ParseSchedule(["15:30", "03:30"]);

        times.Should().HaveCount(2);
        times[0].Should().Be(new TimeOnly(3, 30));
        times[1].Should().Be(new TimeOnly(15, 30));
    }

    /// <summary>
    /// Unparseable configuration falls back to the built-in three-hourly grid,
    /// not to a single daily slot. A worker that silently drops to one run a day
    /// leaves prices up to 24h stale, which is the failure the cadence exists to
    /// prevent — so the fallback has to be the safe-by-default schedule.
    /// </summary>
    [Fact]
    public void ParseSchedule_InvalidEntries_FallBackToTheThreeHourlyGrid()
    {
        var times = SyncWorker.ParseSchedule(["banana", "25:99"]);

        times.Should().HaveCount(8);
        times.Should().Equal(Enumerable.Range(0, 8).Select(i => new TimeOnly(i * 3, 20)));
    }

    [Fact]
    public void TimeUntilNextRun_BeforeFirstSlot_PicksFirstSlotToday()
    {
        var now = new DateTimeOffset(2026, 7, 14, 2, 0, 0, TimeSpan.Zero);
        var schedule = SyncWorker.ParseSchedule(["03:30", "15:30"]);

        SyncWorker.TimeUntilNextRun(now, schedule).Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TimeUntilNextRun_BetweenSlots_PicksSecondSlot()
    {
        var now = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var schedule = SyncWorker.ParseSchedule(["03:30", "15:30"]);

        SyncWorker.TimeUntilNextRun(now, schedule).Should().Be(TimeSpan.FromHours(5.5));
    }

    [Fact]
    public void TimeUntilNextRun_AfterLastSlot_RollsToTomorrow()
    {
        var now = new DateTimeOffset(2026, 7, 14, 23, 0, 0, TimeSpan.Zero);
        var schedule = SyncWorker.ParseSchedule(["03:30", "15:30"]);

        SyncWorker.TimeUntilNextRun(now, schedule).Should().Be(TimeSpan.FromHours(4.5));
    }

    [Fact]
    public void TimeUntilNextRun_ExactlyOnSlot_DoesNotReturnZero()
    {
        var now = new DateTimeOffset(2026, 7, 14, 15, 30, 0, TimeSpan.Zero);
        var schedule = SyncWorker.ParseSchedule(["15:30"]);

        // The slot at 'now' is not strictly in the future → next day.
        SyncWorker.TimeUntilNextRun(now, schedule).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TimeUntilNextRun_IsAlwaysPositive()
    {
        var schedule = SyncWorker.ParseSchedule(["00:00", "12:00", "23:59"]);
        for (var hour = 0; hour < 24; hour++)
        {
            var now = new DateTimeOffset(2026, 7, 14, hour, 17, 33, TimeSpan.Zero);
            SyncWorker.TimeUntilNextRun(now, schedule).Should().BePositive();
        }
    }

    // ── Generated cadence (SyncOptions.IntervalMinutes) ─────────────────────

    [Fact]
    public void BuildSchedule_HourlyInterval_ProducesTwentyFourAnchoredSlots()
    {
        var schedule = SyncWorker.BuildSchedule(
            new SyncOptions { IntervalMinutes = 60, IntervalAnchorMinute = 20 });

        schedule.Should().HaveCount(24);
        schedule.Should().Equal(Enumerable.Range(0, 24).Select(h => new TimeOnly(h, 20)));
    }

    /// <summary>
    /// The whole point of the cadence: consecutive slots are exactly the
    /// configured gap apart, so "every 60 minutes" is a property of the
    /// schedule rather than of 24 hand-written strings that could drift.
    /// </summary>
    [Fact]
    public void BuildSchedule_EverySlotIsExactlyOneIntervalApart()
    {
        var schedule = SyncWorker.BuildSchedule(new SyncOptions { IntervalMinutes = 60 });

        for (var i = 1; i < schedule.Count; i++)
            (schedule[i] - schedule[i - 1]).Should().Be(TimeSpan.FromMinutes(60));

        // And the wrap from the last slot of the day to the first of the next.
        (TimeSpan.FromDays(1) - (schedule[^1] - schedule[0]))
            .Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void BuildSchedule_ZeroInterval_FallsBackToTheListedTimes()
    {
        var schedule = SyncWorker.BuildSchedule(
            new SyncOptions { IntervalMinutes = 0, ScheduleUtc = ["03:30", "15:30"] });

        schedule.Should().Equal(new TimeOnly(3, 30), new TimeOnly(15, 30));
    }

    /// <summary>
    /// A cadence of a day or more cannot describe a daily grid. Emitting the
    /// single anchor slot would silently drop the worker to one run a day —
    /// the same failure ParseSchedule's fallback exists to prevent.
    /// </summary>
    [Fact]
    public void BuildSchedule_IntervalOfADayOrMore_FallsBackRatherThanEmittingOneSlot()
    {
        var schedule = SyncWorker.BuildSchedule(
            new SyncOptions { IntervalMinutes = 1440, ScheduleUtc = ["06:00", "18:00"] });

        schedule.Should().Equal(new TimeOnly(6, 0), new TimeOnly(18, 0));
    }

    /// <summary>
    /// Generating forward from the anchor alone leaves everything before it
    /// uncovered; a late anchor would then mean a single run a day.
    /// </summary>
    [Fact]
    public void BuildSchedule_LateAnchor_StillCoversTheWholeDay()
    {
        var schedule = SyncWorker.BuildSchedule(
            new SyncOptions { IntervalMinutes = 60, IntervalAnchorMinute = 50 });

        schedule.Should().HaveCount(24);
        schedule[0].Should().Be(new TimeOnly(0, 50));
        schedule[^1].Should().Be(new TimeOnly(23, 50));
    }

    [Fact]
    public void BuildSchedule_HourlyGrid_NeverWaitsLongerThanTheInterval()
    {
        var schedule = SyncWorker.BuildSchedule(new SyncOptions { IntervalMinutes = 60 });

        for (var hour = 0; hour < 24; hour++)
        {
            var now = new DateTimeOffset(2026, 7, 14, hour, 37, 11, TimeSpan.Zero);
            var delay = SyncWorker.TimeUntilNextRun(now, schedule);

            delay.Should().BePositive();
            delay.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(60));
        }
    }
}
