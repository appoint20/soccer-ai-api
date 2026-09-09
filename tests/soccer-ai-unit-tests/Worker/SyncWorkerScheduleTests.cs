using FluentAssertions;
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
}
