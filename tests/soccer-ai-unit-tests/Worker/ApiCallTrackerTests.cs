using FluentAssertions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Worker;

/// <summary>
/// The distinction this tracker exists to make: a run that wrote nothing
/// because nothing changed looks exactly like a run that wrote nothing because
/// every request was rejected. Only the call outcomes tell them apart.
/// </summary>
public class ApiCallTrackerTests
{
    [Fact]
    public void A_run_that_made_no_calls_has_not_failed()
    {
        ApiCallStats.Empty.AllFailed.Should().BeFalse();
    }

    [Fact]
    public void All_failed_is_true_only_when_nothing_succeeded()
    {
        var sut = new ApiCallTracker();
        sut.RecordFailure("API key rejected (403)");
        sut.RecordFailure("API key rejected (403)");

        var stats = sut.Current;
        stats.Attempted.Should().Be(2);
        stats.Failed.Should().Be(2);
        stats.Succeeded.Should().Be(0);
        stats.AllFailed.Should().BeTrue();
        stats.LastError.Should().Be("API key rejected (403)");
    }

    /// <summary>
    /// One good call means the credential works, so a quiet day must not be
    /// reported as an outage.
    /// </summary>
    [Fact]
    public void A_single_success_clears_the_all_failed_verdict()
    {
        var sut = new ApiCallTracker();
        sut.RecordFailure("HTTP 500");
        sut.RecordSuccess();

        sut.Current.AllFailed.Should().BeFalse();
        sut.Current.Succeeded.Should().Be(1);
    }

    [Fact]
    public void Reset_clears_counters_between_runs()
    {
        var sut = new ApiCallTracker();
        sut.RecordFailure("API key rejected (403)");
        sut.Reset();

        sut.Current.Should().Be(ApiCallStats.Empty);
        sut.Current.AllFailed.Should().BeFalse();
    }

    [Fact]
    public void Counts_are_stable_under_concurrent_recording()
    {
        var sut = new ApiCallTracker();

        Parallel.For(0, 1000, i =>
        {
            if (i % 2 == 0) sut.RecordSuccess();
            else sut.RecordFailure("HTTP 500");
        });

        sut.Current.Attempted.Should().Be(1000);
        sut.Current.Failed.Should().Be(500);
    }
}
