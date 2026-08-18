using SoccerAi.Application.Services.Sync;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Worker;

namespace soccer_ai_unit_tests.Worker;

/// <summary>
/// Production logged "Schedule (UTC): 03:30, 15:30, 03:30, 15:30" — four slots
/// from two configured values. The configuration binder appends bound array
/// entries to whatever the property already holds rather than replacing them,
/// so any default on <see cref="SyncOptions.ScheduleUtc"/> is concatenated with
/// the configured value and can never be overridden.
/// </summary>
public class SyncOptionsBindingTests
{
    private static SyncOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));

        return services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SyncOptions>>()
            .Value;
    }

    [Fact]
    public void Configured_schedule_replaces_rather_than_appends()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Sync:ScheduleUtc:0"] = "03:30",
            ["Sync:ScheduleUtc:1"] = "15:30"
        });

        options.ScheduleUtc.Should().Equal("03:30", "15:30");
    }

    /// <summary>
    /// The case that mattered operationally: changing the schedule from
    /// configuration must actually change it, not add to a hidden default.
    /// </summary>
    [Fact]
    public void A_single_configured_time_is_the_only_time()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Sync:ScheduleUtc:0"] = "06:00"
        });

        options.ScheduleUtc.Should().Equal("06:00");
        SyncWorker.ParseSchedule(options.ScheduleUtc)
            .Should().Equal(new TimeOnly(6, 0));
    }

    [Fact]
    public void Unconfigured_schedule_falls_back_to_the_parser_default()
    {
        var options = Bind([]);

        options.ScheduleUtc.Should().BeEmpty();
        SyncWorker.ParseSchedule(options.ScheduleUtc)
            .Should().Equal(new TimeOnly(15, 30));
    }

    [Fact]
    public void Other_sync_defaults_still_apply_when_absent()
    {
        var options = Bind([]);

        options.StartupSyncThresholdHours.Should().Be(20);
        options.OddsCaptureIntervalMinutes.Should().Be(30);
    }
}
