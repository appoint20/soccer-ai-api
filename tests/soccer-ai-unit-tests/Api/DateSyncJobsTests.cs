using FluentAssertions;
using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Api.Automation;
using SoccerAi.Api.Controllers;
using SoccerAi.Application.Models;
using soccer_ai_unit_tests.Services;

namespace soccer_ai_unit_tests.Api;

public class DateSyncJobsTests
{
    private static DateSyncJobs Build(DateSyncPipelineTests.Harness h, ManualAutomationGate? gate = null, CancellationToken ct = default) =>
        new(h.Pipeline, gate ?? new(), Mock.Of<IHostApplicationLifetime>(l => l.ApplicationStopping == ct), NullLogger<DateSyncJobs>.Instance);

    private static AutomationController Controller() => new(Mock.Of<IMediator>(), Mock.Of<IHostApplicationLifetime>(), NullLogger<AutomationController>.Instance);

    private static async Task<DateSyncJob> Finished(DateSyncJobs jobs, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.Get(id)!.State == "running") await Task.Delay(10, timeout.Token);
        return jobs.Get(id)!;
    }

    [Fact]
    public async Task StartReturns202AndJobSpecificLocationAndFinishedReport()
    {
        using var h = new DateSyncPipelineTests.Harness();
        var jobs = Build(h);
        var response = (AcceptedResult)Controller().RunSyncForDate(jobs, DateSyncPipelineTests.Day.ToString("yyyy-MM-dd"), true);
        response.StatusCode.Should().Be(202);
        var id = Guid.Parse(response.Location!.Split('/').Last());
        var done = await Finished(jobs, id);
        done.State.Should().Be("completed"); done.ForceAi.Should().BeTrue();
        done.Steps.Should().OnlyContain(s => s.State == "completed");
        Controller().GetDateSyncJob(id, jobs).Should().BeOfType<OkObjectResult>();
        Controller().GetDateSyncJob(Guid.NewGuid(), jobs).Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("2026/09/20")]
    [InlineData("2026-02-30")] [InlineData("2000-01-01")] [InlineData("9999-12-31")]
    public void InvalidDatesDoNotStartAnyWork(string? date)
    {
        using var h = new DateSyncPipelineTests.Harness();
        var jobs = Build(h);
        Controller().RunSyncForDate(jobs, date).Should().BeOfType<BadRequestObjectResult>();
        jobs.Current.Should().BeNull(); h.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GateBlocksBothAnotherDateAndTheAiOnlyEndpointUntilFinished()
    {
        using var h = new DateSyncPipelineTests.Harness();
        var release = new TaskCompletionSource<AiSyncReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Ai.Setup(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(release.Task);
        var gate = new ManualAutomationGate();
        var jobs = Build(h, gate);
        var aiJobs = new AiAnalysisJobs(h.Services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHostApplicationLifetime>(), NullLogger<AiAnalysisJobs>.Instance, gate);
        var first = jobs.TryStart(DateSyncPipelineTests.Day, false)!;
        Controller().RunSyncForDate(jobs, DateSyncPipelineTests.Day.ToString("yyyy-MM-dd")).Should().BeOfType<ConflictObjectResult>();
        aiJobs.TryStart(DateSyncPipelineTests.Day, true).Should().BeNull();
        release.SetResult(new());
        await Finished(jobs, first.Id);
        var next = jobs.TryStart(DateSyncPipelineTests.Day, false)!;
        next.Should().NotBeNull(); await Finished(jobs, next.Id);
    }

    [Fact]
    public async Task FailureAndPendingStepsAreHonestAndExceptionSecretsAreNotExposed()
    {
        using var h = new DateSyncPipelineTests.Harness();
        h.Ai.Setup(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-provider-key"));
        var jobs = Build(h);
        var done = await Finished(jobs, jobs.TryStart(DateSyncPipelineTests.Day, false)!.Id);
        done.State.Should().Be("failed"); done.Error.Should().Contain("ai_analysis").And.NotContain("secret");
        done.Steps.Single(s => s.Name == "ai_analysis").State.Should().Be("failed");
        done.Steps.Single(s => s.Name == "publish_picks").State.Should().Be("skipped");
        done.FinishedAtUtc.Should().NotBeNull();
        var retry = jobs.TryStart(DateSyncPipelineTests.Day, false)!;
        retry.Should().NotBeNull();
        await Finished(jobs, retry.Id);
    }

    [Fact]
    public async Task ShutdownCancelsTheJobAndReleasesTheGate()
    {
        using var h = new DateSyncPipelineTests.Harness();
        using var stopping = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Ai.Setup(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (DateOnly _, bool _, CancellationToken ct) =>
            { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new AiSyncReport(); });
        var gate = new ManualAutomationGate();
        var jobs = Build(h, gate, stopping.Token);
        var first = jobs.TryStart(DateSyncPipelineTests.Day, false)!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();
        var done = await Finished(jobs, first.Id);
        done.State.Should().Be("cancelled");
        done.Steps.Single(s => s.Name == "ai_analysis").State.Should().Be("cancelled");
        gate.TryEnter().Should().BeTrue(); gate.Exit();
    }
}
