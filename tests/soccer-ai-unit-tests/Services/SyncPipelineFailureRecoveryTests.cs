using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

public sealed class SyncPipelineFailureRecoveryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "soccer-sync-recovery-" + Guid.NewGuid().ToString("N") + ".db");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_forecast_or_essential_step_failure_cannot_poison_sync_status(bool essentialStepFails)
    {
        var fixtures = new Mock<IFixtureSyncService>();
        var ai = new Mock<IAiSyncService>();
        var tracker = new Mock<IApiCallTracker>(); tracker.SetupGet(x => x.Current).Returns(ApiCallStats.Empty);
        var registration = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False"))
            .AddScoped<IApplicationDbContext>(s => s.GetRequiredService<ApplicationDbContext>())
            .AddSingleton(fixtures.Object).AddSingleton(Mock.Of<ITeamSyncService>())
            .AddSingleton(Mock.Of<IGoalRateTrainingService>()).AddSingleton(Mock.Of<IAnalysisPrecomputeService>())
            .AddSingleton(Mock.Of<IPickLedger>()).AddSingleton(Mock.Of<IDailyPickService>()).AddSingleton(ai.Object);
        if (essentialStepFails)
            registration.AddScoped<IAnalysisPrecomputeService>(s => {
                var mock = new Mock<IAnalysisPrecomputeService>(); var db = s.GetRequiredService<ApplicationDbContext>();
                mock.Setup(x => x.RecomputeWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .Returns(async (DateTime from, DateTime through, CancellationToken ct) => { await FailSave(db, ct); return 0; });
                return mock.Object;
            });
        registration.AddScoped<IModelForecastSyncService>(s => {
            var mock = new Mock<IModelForecastSyncService>(); var db = s.GetRequiredService<ApplicationDbContext>();
            mock.Setup(x => x.RunAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (int days, CancellationToken ct) => { await FailSave(db, ct); return (0, 0); });
            return mock.Object;
        });
        using var services = registration.BuildServiceProvider();
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
            db.ModelForecasts.Add(new ModelForecast { FixtureId = 7, Model = "same-model" });
            // State left by the broken forecast step in the production trace.
            db.SyncStates.Add(new SyncState { Id = 1, LastRunStartedUtc = DateTimeOffset.UtcNow.AddDays(-2),
                LastSuccessfulSyncUtc = DateTimeOffset.UtcNow.AddDays(-3), LastCompletedStep = SyncPipeline.Steps.PublishPicks });
            await db.SaveChangesAsync();
        }
        var pipeline = new SyncPipeline(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SyncOptions { GenerateAiNarratives = true, RecomputeDaysAhead = 0 }), tracker.Object, NullLogger<SyncPipeline>.Instance);
        var completed = await pipeline.RunAsync(true, CancellationToken.None);
        completed.Should().Be(!essentialStepFails);
        fixtures.Verify(x => x.SyncAllLeaguesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        ai.Verify(x => x.SyncUpcomingFixturesAsync(It.IsAny<DateTime>(), false, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            essentialStepFails ? Times.Never() : Times.Once());
        using var verification = services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<ApplicationDbContext>().SyncStates.SingleAsync();
        if (essentialStepFails)
        {
            stored.LastError.Should().NotBeNullOrWhiteSpace();
            stored.LastCompletedStep.Should().Be(SyncPipeline.Steps.TrainModel);
        }
        else
        {
            stored.LastError.Should().BeNull(); stored.LastCompletedStep.Should().Be(SyncPipeline.Steps.AiNarratives);
            stored.LastSuccessfulSyncUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
            // A later run must still refresh football data after an optional failure.
            (await pipeline.RunAsync(true, CancellationToken.None)).Should().BeTrue();
            fixtures.Verify(x => x.SyncAllLeaguesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    private static async Task FailSave(ApplicationDbContext db, CancellationToken ct)
    {
        db.ModelForecasts.Add(new ModelForecast { FixtureId = 7, Model = "same-model" });
        await db.SaveChangesAsync(ct); // Real unique-key error leaves an Added entity tracked.
    }
    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
    }
}
