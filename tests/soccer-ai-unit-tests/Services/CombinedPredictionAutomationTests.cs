using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Infrastructure;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;
using SoccerAi.Worker;

namespace soccer_ai_unit_tests.Services;

public class CombinedPredictionAutomationTests
{
    [Theory]
    [InlineData(30, null, null)]
    [InlineData(1500, null, null)]
    [InlineData(600, null, "daily:2026-10-01")]
    [InlineData(600, "2026-10-01T00:00:00Z", null)]
    [InlineData(600, "2026-09-30T23:59:59Z", "daily:2026-10-01")]
    [InlineData(120, null, "final")]
    [InlineData(120, "2026-10-01T09:00:00Z", null)]
    [InlineData(120, "2026-10-01T10:00:00Z", null)]
    [InlineData(240, "2026-10-01T09:00:00Z", "final")]
    [InlineData(240, "2026-10-01T10:00:00Z", null)]
    public void ScheduleUsesUtcDailyAndOneFinalWindowWithKickoffCutoff(int minutes, string? captured, string? expected)
    {
        var now = DateTimeOffset.Parse("2026-10-01T12:00:00+02:00");
        CombinedPredictionAutomationSchedule.WindowFor(now.AddMinutes(minutes), now,
            captured is null ? null : DateTimeOffset.Parse(captured), new()).Should().Be(expected);
    }

    [Fact]
    public async Task DisabledOrBlockedAutomationMakesNoCallsAndConsumesNoBudget()
    {
        await using var harness = await Harness.Create();
        harness.Settings.Enabled = false;
        (await harness.Run()).Status.Should().Be("disabled");
        harness.Readiness.VerifyNoOtherCalls();
        harness.Settings.Enabled = true;
        harness.Readiness.Setup(service => service.BlockingReasonAsync(It.IsAny<CancellationToken>())).ReturnsAsync("Missing credential");
        (await harness.Run()).Status.Should().Be("blocked");
        harness.Refresh.VerifyNoOtherCalls();
        (await harness.Attempts()).Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshCapsAndDuplicateProtectionSurviveNewServiceScopes()
    {
        await using var harness = await Harness.Create();
        (await harness.Run()).Completed.Should().Be(3);
        (await harness.Run()).Completed.Should().Be(2);
        (await harness.Run()).Status.Should().Be("daily_limit");
        var rows = await harness.Attempts();
        rows.Should().HaveCount(5).And.OnlyContain(row => row.Status == "completed" && row.SnapshotId != null);
        rows.Select(row => row.FixtureId).Distinct().Should().HaveCount(5);
        harness.Refresh.Verify(service => service.RefreshAsync(It.IsAny<int>(), true, It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task DailyCaptureStillAllowsOneFinalRefreshAndRescheduledKickoffsGetANewWindow()
    {
        await using var harness = await Harness.Create();
        harness.Settings.MaxRefreshesPerRun = 1;
        (await harness.Run()).Completed.Should().Be(1);
        using (var scope = harness.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Fixtures.Where(fixture => fixture.Id == 1)
                .ExecuteUpdateAsync(update => update.SetProperty(fixture => fixture.Date, harness.Clock.Now.AddHours(5).AddMinutes(10)));
        }
        (await harness.Run()).Completed.Should().Be(1);
        harness.Clock.Now = harness.Clock.Now.AddHours(1).AddMinutes(10);
        (await harness.Run()).Completed.Should().Be(1);
        var attempts = await harness.Attempts();
        attempts.Should().HaveCount(3).And.OnlyContain(attempt => attempt.FixtureId == 1);
        attempts.Count(attempt => attempt.Window == "final").Should().Be(1);
        attempts.Select(attempt => attempt.KickoffUtc).Distinct().Should().HaveCount(2);
        await harness.Run();
        harness.Refresh.Verify(service => service.RefreshAsync(1, true, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task FailedAttemptIsPersistedWithoutSecretsAndNotRetriedInTheSameWindow()
    {
        await using var harness = await Harness.Create();
        harness.Refresh.Setup(service => service.RefreshAsync(1, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive-provider-secret"));
        var first = await harness.Run();
        first.Failed.Should().Be(1);
        first.Attempted.Should().Be(1);
        var failed = (await harness.Attempts()).Single();
        failed.Status.Should().Be("failed");
        failed.Error.Should().NotContain("sensitive-provider-secret");
        (await harness.Run()).Completed.Should().Be(3);
        harness.Refresh.Verify(service => service.RefreshAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PartialForecastIsStoredAsPartialAndStopsTheCurrentBatch()
    {
        await using var harness = await Harness.Create();
        harness.Refresh.Setup(service => service.RefreshAsync(It.IsAny<int>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombinedPredictionRefreshResult(1, Guid.NewGuid(),
                new CombinedPrediction(1, harness.Clock.Now.AddHours(6), harness.Clock.Now, new(), []), false, ["missing AI"]));
        var report = await harness.Run();
        report.Partial.Should().Be(1);
        report.Completed.Should().Be(0);
        report.Status.Should().Be("completed_with_warnings");
        (await harness.Attempts()).Single().Status.Should().Be("partial");
    }

    [Fact]
    public async Task ShutdownKeepsItsReservationAndRecordsCancellation()
    {
        await using var harness = await Harness.Create();
        using var cancellation = new CancellationTokenSource();
        harness.Refresh.Setup(service => service.RefreshAsync(It.IsAny<int>(), true, It.IsAny<CancellationToken>()))
            .Returns((int _, bool _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<CombinedPredictionRefreshResult>(token);
            });
        var run = () => harness.Run(cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        (await harness.Attempts()).Single().Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task RecentManualSnapshotsAndAlreadyStartedFixturesAreSkipped()
    {
        await using var harness = await Harness.Create();
        using (var scope = harness.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fixtures = await db.Fixtures.ToListAsync();
            foreach (var fixture in fixtures)
            {
                if (fixture.Id % 2 == 0) fixture.Status = "1H";
                else db.CombinedPredictionSnapshots.Add(new()
                {
                    FixtureId = fixture.Id, KickoffUtc = fixture.Date, CapturedAtUtc = harness.Clock.Now, PredictionJson = "{}", EvidenceJson = "{}"
                });
            }
            await db.SaveChangesAsync();
        }
        (await harness.Run()).Status.Should().Be("idle");
        harness.Refresh.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReservationPreventsOverlappingWorkersAndResetsOnlyTheUtcDayBudget()
    {
        await using var harness = await Harness.Create();
        using (var scope = harness.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Fixtures.ExecuteUpdateAsync(update => update.SetProperty(fixture => fixture.Date, harness.Clock.Now.AddDays(3)));
        }
        var first = harness.Attempt(1);
        var second = harness.Attempt(2);
        async Task<bool> Reserve(CombinedPredictionAutomationAttempt attempt)
        {
            using var scope = harness.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomationStore>().TryReserveAsync(attempt, 1, default);
        }
        var reserved = await Task.WhenAll(Task.Run(() => Reserve(first)), Task.Run(() => Reserve(second)));
        reserved.Count(value => value).Should().Be(1);
        var winner = reserved[0] ? first : second;
        using (var scope = harness.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomationStore>()
                .FinishAsync(winner.Id, "failed", null, "test", harness.Clock.Now, default);
        (await Reserve(harness.Attempt(3))).Should().BeFalse();
        harness.Clock.Now = harness.Clock.Now.AddDays(1);
        var nextDay = harness.Attempt(3);
        nextDay.KickoffUtc = first.KickoffUtc;
        (await Reserve(nextDay)).Should().BeTrue();
        (await harness.Attempts()).Should().HaveCount(2);
    }

    [Fact]
    public async Task AbandonedRunningWindowIsNotRepeatedAfterAWorkerRestart()
    {
        await using var harness = await Harness.Create();
        using var scope = harness.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = await db.Fixtures.SingleAsync(fixture => fixture.Id == 1);
        db.CombinedPredictionAutomationAttempts.Add(new()
        {
            FixtureId = 1, KickoffUtc = first.Date, Window = "daily:2026-10-01", StartedAtUtc = harness.Clock.Now.AddHours(-2)
        });
        await db.SaveChangesAsync();
        (await harness.Run()).Completed.Should().Be(3);
        var rows = await harness.Attempts();
        rows.Single(row => row.FixtureId == 1).Status.Should().Be("interrupted");
        harness.Refresh.Verify(service => service.RefreshAsync(1, true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadinessRequiresAcceptedMlAndRejectsPaidOrWrongProviderCredentials()
    {
        await using var harness = await Harness.Create();
        using var scope = harness.Services.CreateScope();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiFootball:ApiKey"] = "test-football-key", ["AiService:ApiKey"] = "sk-or-test-key"
        }).Build();
        var narration = new AiServiceOptions { ApiKey = "sk-or-test-key" };
        var router = new OpenRouterOptions { ApiKey = "sk-or-test-key" };
        var readiness = new CombinedPredictionAutomationReadiness(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            configuration, Options.Create(harness.Settings), Options.Create(router), Options.Create(narration), Options.Create(new HybridModelOptions()));
        (await readiness.BlockingReasonAsync(default)).Should().Contain("accepted ML");
        harness.Settings.RequireAcceptedMl = false;
        (await readiness.BlockingReasonAsync(default)).Should().BeNull();
        narration.FallbackModel = "paid/model";
        (await readiness.BlockingReasonAsync(default)).Should().Contain("free");
        router.ApiKey = "wrong-provider-key";
        (await readiness.BlockingReasonAsync(default)).Should().Contain("Numerical AI");
    }

    [Fact]
    public void InvalidAutomationSettingsFailValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CombinedPredictionAutomation:PollIntervalMinutes"] = "0"
        }).Build();
        using var services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        var read = () => services.GetRequiredService<IOptions<CombinedPredictionAutomationOptions>>().Value;
        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task DisabledWorkerDoesNotResolveAnyService()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker = new CombinedPredictionWorker(scopes.Object,
            Options.Create(new CombinedPredictionAutomationOptions()), NullLogger<CombinedPredictionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WorkerRunsOnceAndStopsWithoutWaitingForTheNextInterval()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<ICombinedPredictionAutomation>();
        service.Setup(value => value.RunAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            entered.TrySetResult();
            return Task.FromResult(new CombinedPredictionAutomationReport("idle"));
        });
        using var provider = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        using var worker = new CombinedPredictionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CombinedPredictionAutomationOptions { Enabled = true }), NullLogger<CombinedPredictionWorker>.Instance);
        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        service.Verify(value => value.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "soccer-automation-" + Guid.NewGuid().ToString("N") + ".db");
        public TestClock Clock { get; } = new();
        public CombinedPredictionAutomationOptions Settings { get; } = new() { Enabled = true };
        public Mock<ICombinedPredictionAutomationReadiness> Readiness { get; } = new();
        public Mock<ICombinedPredictionRefreshService> Refresh { get; } = new();
        public ServiceProvider Services { get; private set; } = null!;

        public static async Task<Harness> Create()
        {
            var harness = new Harness();
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={harness._path};Pooling=False")
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<TimeProvider>(harness.Clock);
            services.AddSingleton(Options.Create(harness.Settings));
            services.AddSingleton(harness.Readiness.Object);
            services.AddSingleton(harness.Refresh.Object);
            services.AddScoped<ICombinedPredictionAutomationStore, CombinedPredictionAutomationStore>();
            services.AddScoped<ICombinedPredictionAutomation, CombinedPredictionAutomationService>();
            harness.Services = services.BuildServiceProvider();
            harness.Refresh.Setup(service => service.RefreshAsync(It.IsAny<int>(), true, It.IsAny<CancellationToken>()))
                .Returns(async (int id, bool _, CancellationToken ct) =>
                {
                    using var scope = harness.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var fixture = await db.Fixtures.SingleAsync(row => row.Id == id, ct);
                    var markets = new CombinedMarkets { HomeWin = .45, Draw = .3, AwayWin = .25, Btts = .6, Over25 = .65,
                        TwoToThreeGoals = .5, BttsAndOver25 = .4, ExpectedGoals = 2.8 };
                    var prediction = new CombinedPrediction(id, fixture.Date, harness.Clock.Now, markets,
                        new[] { "historical", "ml", "provider", "ai" }.Select(name => new PredictionSource(name, "test", markets,
                            CapturedAtUtc: harness.Clock.Now, OutcomeWeight: .25, GoalsWeight: .25)).ToArray());
                    var snapshot = new CombinedPredictionSnapshot { FixtureId = id, KickoffUtc = fixture.Date,
                        CapturedAtUtc = harness.Clock.Now, PredictionJson = JsonSerializer.Serialize(prediction), EvidenceJson = "{}" };
                    db.CombinedPredictionSnapshots.Add(snapshot);
                    await db.SaveChangesAsync(ct);
                    return new CombinedPredictionRefreshResult(id, snapshot.Id, prediction, true, []);
                });
            using var seed = harness.Services.CreateScope();
            var context = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();
            context.Teams.AddRange(new Team { ApiId = 10, Name = "Home" }, new Team { ApiId = 20, Name = "Away" });
            for (var fixtureId = 1; fixtureId <= 8; fixtureId++)
                context.Fixtures.Add(new() { Id = fixtureId, ApiId = fixtureId, Date = harness.Clock.Now.AddHours(fixtureId + 5),
                    Status = "NS", HomeTeamId = 10, AwayTeamId = 20 });
            await context.SaveChangesAsync();
            return harness;
        }

        public async Task<CombinedPredictionAutomationReport> Run(CancellationToken ct = default)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomation>().RunAsync(ct);
        }

        public async Task<List<CombinedPredictionAutomationAttempt>> Attempts()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CombinedPredictionAutomationAttempts.AsNoTracking().ToListAsync();
        }

        public CombinedPredictionAutomationAttempt Attempt(int fixtureId) => new()
        {
            FixtureId = fixtureId, KickoffUtc = Clock.Now.AddDays(3), StartedAtUtc = Clock.Now,
            Window = "daily:" + Clock.Now.ToString("yyyy-MM-dd")
        };

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
        }
    }
}
