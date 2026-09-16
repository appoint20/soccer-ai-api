using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Api.Automation;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The background runner behind POST /api/automation/ai-analysis: one job at a
/// time, and a poller always ends up with an honest terminal state.
/// </summary>
public class AiAnalysisJobsTests
{
    private static readonly DateOnly Day = new(2026, 9, 20);

    private static AiAnalysisJobs Build(Mock<IAiSyncService> sync)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => sync.Object)
            .BuildServiceProvider();

        return new AiAnalysisJobs(
            services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHostApplicationLifetime>(l => l.ApplicationStopping == CancellationToken.None),
            NullLogger<AiAnalysisJobs>.Instance, new ManualAutomationGate());
    }

    private static async Task<AiAnalysisJob> FinishedAsync(AiAnalysisJobs jobs, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var job = jobs.Get(id)!;
            if (job.State != AiAnalysisJob.Running) return job;
            await Task.Delay(10);
        }

        throw new TimeoutException($"Job {id} did not finish.");
    }

    [Fact]
    public async Task AFinishedJobCarriesItsReport()
    {
        var sync = new Mock<IAiSyncService>();
        sync.Setup(s => s.SyncDateAsync(Day, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiSyncReport { FixturesOnDate = 12, Candidates = 9, Generated = 9 });
        var jobs = Build(sync);

        var started = jobs.TryStart(Day, force: true)!;
        started.State.Should().Be(AiAnalysisJob.Running);

        var done = await FinishedAsync(jobs, started.Id);
        done.State.Should().Be(AiAnalysisJob.Completed);
        done.Report!.Generated.Should().Be(9);
        done.FinishedAtUtc.Should().NotBeNull();
        done.Error.Should().BeNull();
    }

    [Fact]
    public async Task OnlyOneJobRunsAtATime()
    {
        var release = new TaskCompletionSource<AiSyncReport>();
        var sync = new Mock<IAiSyncService>();
        sync.Setup(s => s.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var jobs = Build(sync);

        var first = jobs.TryStart(Day, force: false)!;

        jobs.TryStart(Day.AddDays(1), force: false).Should().BeNull();
        jobs.Current!.Id.Should().Be(first.Id);

        release.SetResult(new AiSyncReport());
        await FinishedAsync(jobs, first.Id);

        // Seeing the job finish is enough to start the next one — no retry loop.
        jobs.TryStart(Day.AddDays(1), force: false).Should().NotBeNull();
    }

    /// <summary>A missing key must read as a failure with the reason, never as an empty success.</summary>
    [Fact]
    public async Task AFailureIsRecordedWithItsReasonAndFreesTheGate()
    {
        var sync = new Mock<IAiSyncService>();
        sync.Setup(s => s.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalApiException("AI narratives", "the configured provider key is missing",
                System.Net.HttpStatusCode.ServiceUnavailable));
        var jobs = Build(sync);

        var job = jobs.TryStart(Day, force: false)!;
        var done = await FinishedAsync(jobs, job.Id);

        done.State.Should().Be(AiAnalysisJob.Failed);
        done.Error.Should().Contain("provider key is missing");
        done.Report.Should().BeNull();
        jobs.TryStart(Day, force: false).Should().NotBeNull();
    }

    [Fact]
    public async Task APartialRunIsNotReportedAsCleanSuccess()
    {
        var sync = new Mock<IAiSyncService>();
        sync.Setup(s => s.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiSyncReport { Candidates = 5, Generated = 3, Failed = 2, FailedFixtureIds = [11, 12] });
        var jobs = Build(sync);

        var done = await FinishedAsync(jobs, jobs.TryStart(Day, force: false)!.Id);

        done.State.Should().Be(AiAnalysisJob.CompletedWithFailures);
        done.Report!.FailedFixtureIds.Should().Equal(11, 12);
    }

    [Fact]
    public void AnUnknownJobIsNotFound()
    {
        Build(new Mock<IAiSyncService>()).Get(Guid.NewGuid()).Should().BeNull();
    }
}
