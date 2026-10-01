using System.Reflection;
using FluentAssertions;
using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Api.Automation;
using SoccerAi.Api.Controllers;
using SoccerAi.Api.Security;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Api;

public class CombinedPredictionJobsTests
{
    [Fact]
    public async Task EndpointReturnsPollingUrlAndSerializesAutomationWithHonestPartialStatus()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Fixtures.Add(new Fixture { Id = 1, Date = DateTimeOffset.UtcNow.AddDays(1), Status = "NS" });
        await db.SaveChangesAsync();
        var pending = new TaskCompletionSource<CombinedPredictionRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<ICombinedPredictionRefreshService>();
        service.Setup(value => value.RefreshAsync(1, true, It.IsAny<CancellationToken>())).Returns(pending.Task);
        await using var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var gate = new ManualAutomationGate();
        var jobs = new CombinedPredictionJobs(services.GetRequiredService<IServiceScopeFactory>(), gate,
            Mock.Of<IHostApplicationLifetime>(), NullLogger<CombinedPredictionJobs>.Instance);
        var controller = new AutomationController(Mock.Of<IMediator>(), Mock.Of<IHostApplicationLifetime>(), NullLogger<AutomationController>.Instance);
        var accepted = (AcceptedResult)await controller.RefreshPrediction(1, db, jobs);
        var identifier = Guid.Parse(accepted.Location!.Split('/').Last());
        accepted.StatusCode.Should().Be(202);
        (await controller.RefreshPrediction(1, db, jobs)).Should().BeOfType<ConflictObjectResult>();
        pending.SetResult(new CombinedPredictionRefreshResult(1, Guid.NewGuid(),
            new CombinedPrediction(1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow, new CombinedMarkets(), []), false, ["AI missing"]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.Get(identifier)!.State == "running") await Task.Delay(10, timeout.Token);
        jobs.Get(identifier)!.State.Should().Be("completed_with_warnings");
        jobs.Get(identifier)!.Result!.Warnings.Should().Contain("AI missing");
        controller.GetPredictionRefreshJob(identifier, jobs).Should().BeOfType<OkObjectResult>();
        controller.GetPredictionRefreshJob(Guid.NewGuid(), jobs).Should().BeOfType<NotFoundObjectResult>();
        gate.TryEnter().Should().BeTrue();
        gate.Exit();
    }

    [Fact]
    public async Task MissingOrStartedFixturesNeverStartProviderWork()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Fixtures.Add(new Fixture { Id = 1, Date = DateTimeOffset.UtcNow.AddHours(-1), Status = "FT" });
        await db.SaveChangesAsync();
        var service = new Mock<ICombinedPredictionRefreshService>(MockBehavior.Strict);
        await using var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var jobs = new CombinedPredictionJobs(services.GetRequiredService<IServiceScopeFactory>(), new ManualAutomationGate(),
            Mock.Of<IHostApplicationLifetime>(), NullLogger<CombinedPredictionJobs>.Instance);
        var controller = new AutomationController(Mock.Of<IMediator>(), Mock.Of<IHostApplicationLifetime>(), NullLogger<AutomationController>.Instance);
        (await controller.RefreshPrediction(99, db, jobs)).Should().BeOfType<NotFoundObjectResult>();
        (await controller.RefreshPrediction(1, db, jobs)).Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JobErrorsDoNotExposeProviderSecrets()
    {
        var service = new Mock<ICombinedPredictionRefreshService>();
        service.Setup(value => value.RefreshAsync(1, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-key-and-connection-string"));
        await using var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var gate = new ManualAutomationGate();
        var jobs = new CombinedPredictionJobs(services.GetRequiredService<IServiceScopeFactory>(), gate,
            Mock.Of<IHostApplicationLifetime>(), NullLogger<CombinedPredictionJobs>.Instance);
        var job = jobs.TryStart(1, false)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.Get(job.Id)!.State == "running") await Task.Delay(10, timeout.Token);
        jobs.Get(job.Id)!.State.Should().Be("failed");
        jobs.Get(job.Id)!.Error.Should().NotContain("secret-key");
        gate.TryEnter().Should().BeTrue();
        gate.Exit();
    }

    [Theory]
    [InlineData(nameof(AutomationController.RefreshPrediction))]
    [InlineData(nameof(AutomationController.GetPredictionRefreshJob))]
    public void RefreshAndStatusEndpointsRequireTheAdminApiKey(string method)
    {
        typeof(AutomationController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>()
            .Should().Contain(attribute => attribute.Policy == AdminApiKeyAuthenticationDefaults.PolicyName);
    }
}
