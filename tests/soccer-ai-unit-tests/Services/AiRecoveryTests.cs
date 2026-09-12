using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;
using SoccerAi.Tools;

namespace soccer_ai_unit_tests.Services;

public class AiRecoveryTests
{
    [Fact]
    public void BlankConfiguredKeyFallsThroughOnlyToTheMatchingProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AiService:ApiKey"] = "", ["OPENROUTER_API_KEY"] = "  router-key  ", ["ZAI_API_KEY"] = "other-key"
        }).Build();
        AiCredentials.Resolve(configuration, environment: _ => null).Should().Be("router-key");
        configuration["OPENROUTER_API_KEY"] = "";
        AiCredentials.Resolve(configuration, environment: _ => null).Should().BeEmpty();
        AiCredentials.Resolve(configuration, baseUrl: "https://api.z.ai/api/paas/v4", environment: _ => null).Should().Be("other-key");
    }

    [Fact]
    public async Task DisabledAndIncorrectlyConfiguredAiCannotReportAnEmptySuccess()
    {
        var disabled = new DisabledAiAnalysisService(NullLogger<DisabledAiAnalysisService>.Instance);
        var runDisabled = () => disabled.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 1 }]);
        await runDisabled.Should().ThrowAsync<ExternalApiException>();
        var service = new OpenAiAnalysisService(Options.Create(new AiServiceOptions { ApiKey = "wrong-provider-key" }),
            new ConfigurationBuilder().Build(), NullLogger<OpenAiAnalysisService>.Instance);
        var run = () => service.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 1 }]);
        (await run.Should().ThrowAsync<ExternalApiException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void BilingualPayloadValidationAndCacheRepairRequireRealText()
    {
        var result = ValidResult();
        AiNarrativeIntegrity.InvalidResult(result).Should().BeNull();
        result.De.Analysis = " "; AiNarrativeIntegrity.InvalidResult(result).Should().NotBeNull();
        result = ValidResult(); result.Over25Qualified = true; result.Under25Qualified = true;
        AiNarrativeIntegrity.InvalidResult(result).Should().NotBeNull();
        var row = new FixtureAnalysis { Analysis = "New saved analysis" };
        var old = new MatchAnalysis { Ai = new AiAnalysisDto { Analysis = "" } };
        AiNarrativeIntegrity.NeedsSnapshotRefresh(old, row).Should().BeTrue();
        old.Ai = new AiAnalysisDto { Analysis = row.Analysis };
        AiNarrativeIntegrity.NeedsSnapshotRefresh(old, row).Should().BeFalse();
    }

    [Fact]
    public void PostgreSqlUnknownTimestampDoesNotBecomeFalseHistoricalEvidence()
    {
        var row = JsonSerializer.Deserialize<Fixture>("""{"Id":1,"Date":"2026-09-12T12:00:00Z","UpdatedAt":"-infinity"}""", FootballAuditJson.Options)!;
        row.UpdatedAt.Should().BeNull(); row.Date.Year.Should().Be(2026);
        var payload = JsonSerializer.Deserialize<AiBilingualResult>("""{"GeneratedAtUtc":"2020-01-01T00:00:00Z","ModelVersion":"forged"}""")!;
        payload.GeneratedAtUtc.Should().BeNull(); payload.ModelVersion.Should().BeNull();
    }

    [Fact]
    public async Task TargetedSyncRepairsBlankRowsDespitePositiveConfidenceAndPersistsBothLanguages()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var fixture = new Fixture { Id = 1, Date = DateTimeOffset.UtcNow.AddHours(4), Status = "NS", HomeTeamId = 10, AwayTeamId = 20 };
        db.Fixtures.Add(fixture); db.Teams.AddRange(new Team { ApiId = 10 }, new Team { ApiId = 20 });
        db.FixtureAnalyses.AddRange(new FixtureAnalysis { FixtureId = 1, Lang = "en", Confidence = 80 },
            new FixtureAnalysis { FixtureId = 1, Lang = "de", Confidence = 80 });
        await db.SaveChangesAsync();
        var analysis = new Mock<IMatchAnalysisService>();
        analysis.Setup(x => x.AnalyzeFixtureAsync(fixture, "en", true, It.IsAny<CancellationToken>())).ReturnsAsync(new FixtureAnalysisResult {
            FixtureId = 1, TeamStats = new TeamStatsResponse(), Models = new StatisticalModels(), H2H = new HeadToHeadModel(),
            Decisions = new DecisionServiceResult(), LeagueName = "League", Prediction = new WeightedPrediction { HomeProb = .5, Over25Prob = .6 }
        });
        var provider = new Mock<IAiAnalysisService>(); var result = ValidResult();
        result.GeneratedAtUtc = DateTimeOffset.UtcNow; result.ModelVersion = "actual-model";
        provider.Setup(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, AiBilingualResult> { [1] = result });
        var precompute = new Mock<IAnalysisPrecomputeService>();
        var sut = new AiSyncService(db, analysis.Object, provider.Object, precompute.Object, Mock.Of<ILeagueTierService>(), NullLogger<AiSyncService>.Instance);
        await sut.SyncSingleFixtureAsync(1);
        var stored = await db.FixtureAnalyses.ToListAsync();
        stored.Should().HaveCount(2).And.OnlyContain(r => r.Analysis.Length > 0 && r.AiModelVersion == "actual-model");
        precompute.Verify(x => x.RecomputeFixtureAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        await sut.SyncSingleFixtureAsync(1);
        provider.Verify(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AiBilingualResult ValidResult() => new() { FixtureId = 1, Confidence = 70, OverallConfidence = 70,
        En = new AiLanguageBlock { Analysis = "The supplied match data supports this assessment." },
        De = new AiLanguageBlock { Analysis = "Die gelieferten Spieldaten stützen diese Einschätzung." } };

    [Fact]
    public async Task AiOutageFailsSyncStatusWithoutPreventingNextFootballRefresh()
    {
        var name = Guid.NewGuid().ToString();
        var fixtures = new Mock<IFixtureSyncService>();
        var ai = new Mock<IAiSyncService>();
        ai.Setup(x => x.SyncUpcomingFixturesAsync(It.IsAny<DateTime>(), false, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalApiException("OpenRouter", "Credential rejected", System.Net.HttpStatusCode.Unauthorized));
        var tracker = new Mock<IApiCallTracker>(); tracker.SetupGet(x => x.Current).Returns(ApiCallStats.Empty);
        using var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name))
            .AddScoped<IApplicationDbContext>(p => p.GetRequiredService<ApplicationDbContext>())
            .AddSingleton(fixtures.Object).AddSingleton(Mock.Of<ITeamSyncService>())
            .AddSingleton(Mock.Of<IGoalRateTrainingService>()).AddSingleton(Mock.Of<IAnalysisPrecomputeService>())
            .AddSingleton(Mock.Of<IPickLedger>()).AddSingleton(Mock.Of<IDailyPickService>())
            .AddSingleton(Mock.Of<IModelForecastSyncService>()).AddSingleton(ai.Object).BuildServiceProvider();
        var pipeline = new SyncPipeline(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SyncOptions { GenerateAiNarratives = true, RecomputeDaysAhead = 0 }),
            tracker.Object, NullLogger<SyncPipeline>.Instance);
        (await pipeline.RunAsync(true, CancellationToken.None)).Should().BeFalse();
        (await pipeline.RunAsync(true, CancellationToken.None)).Should().BeFalse();
        fixtures.Verify(x => x.SyncAllLeaguesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        using var scope = services.CreateScope();
        var state = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SyncStates.SingleAsync();
        state.LastSuccessfulSyncUtc.Should().BeNull(); state.LastError.Should().Contain("Credential rejected");
        state.LastCompletedStep.Should().Be(SyncPipeline.Steps.ModelForecasts);
    }
}
