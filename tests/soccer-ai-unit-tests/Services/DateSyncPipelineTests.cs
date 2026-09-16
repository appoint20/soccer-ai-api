using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

public class DateSyncPipelineTests
{
    internal static DateOnly Day => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
    internal static DateTimeOffset At(DateOnly day, int hour = 12) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    internal sealed class Harness : IDisposable
    {
        public readonly Mock<ITeamSyncService> Teams = new();
        public readonly Mock<IDateFixtureSyncService> Fixtures = new();
        public readonly Mock<IFixtureSyncService> History = new();
        public readonly Mock<IAnalysisPrecomputeService> Precompute = new();
        public readonly Mock<IAiSyncService> Ai = new();
        public readonly Mock<IDailyPickService> Picks = new();
        public readonly Mock<IPickLedger> Ledger = new();
        public readonly List<string> Calls = [];
        public readonly ServiceProvider Services;
        public readonly DateSyncPipeline Pipeline;
        private readonly string _database = Guid.NewGuid().ToString();

        public Harness(params Fixture[] matches)
        {
            Teams.Setup(x => x.SyncLeagueStandingsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("standings"); return new SyncResult(); });
            Fixtures.Setup(x => x.SyncLeagueForDateAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("fixtures"); return new SyncResult(); });
            Fixtures.Setup(x => x.CaptureDateOddsAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("odds"); return new DateOddsReport(1, 1, 1, 0); });
            History.Setup(x => x.EnsureHistoricalDepthAsync(It.IsAny<int>(), 10, 2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("historical_depth"); return new SyncResult(); });
            Precompute.Setup(x => x.RecomputeFixtureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) =>
                {
                    Calls.Add($"predict:{id}");
                    return ValidPrediction();
                });
            Ai.Setup(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("ai"); return new AiSyncReport { Candidates = 1, Generated = 1 }; });
            Picks.Setup(x => x.GetBoardAsync(It.IsAny<DateOnly>(), "en", It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly date, string _, CancellationToken _) =>
                { Calls.Add("board"); return DailyPickBoard.Empty(date); });
            Ledger.Setup(x => x.RecordAsync(It.IsAny<DailyPickBoard>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { Calls.Add("publish"); return 0; });
            var tiers = new Mock<ILeagueTierService>();
            tiers.Setup(x => x.GetSyncLeagueIds()).Returns([39]);
            Services = new ServiceCollection()
                .AddScoped<IApplicationDbContext>(_ => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(_database).Options))
                .AddSingleton(tiers.Object).AddScoped(_ => Teams.Object).AddScoped(_ => Fixtures.Object)
                .AddScoped(_ => History.Object).AddScoped(_ => Precompute.Object).AddScoped(_ => Ai.Object)
                .AddScoped(_ => Picks.Object).AddScoped(_ => Ledger.Object).BuildServiceProvider();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Fixtures.AddRange(matches);
            db.SaveChangesAsync(default).GetAwaiter().GetResult();
            Pipeline = new(Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DateSyncPipeline>.Instance);
        }

        internal static IReadOnlyDictionary<string, MatchAnalysis> ValidPrediction() => new Dictionary<string, MatchAnalysis>
        {
            ["en"] = new() { Prediction = new(), DecisionAudit = new(0, [], DateTimeOffset.UtcNow),
                Models = new() { ModelVersion = "accepted-test-model", Poisson = new() { IsValid = true } } },
            ["de"] = new()
        };
        public void Dispose() => Services.Dispose();
    }

    internal static Fixture Match(int id, DateTimeOffset date, string status = "NS", int league = 39) =>
        new() { Id = id, ApiId = id + 1000, Date = date, Status = status, LeagueId = league };

    [Fact]
    public async Task OnlyRequestedUpcomingDayIsPredictedAndPublicationFollowsAiAndFinalRecompute()
    {
        using var h = new Harness(Match(1, At(Day, 0)), Match(2, At(Day.AddDays(1), 0)),
            Match(3, At(Day), "FT"), Match(4, At(Day), league: 999), Match(5, At(Day.AddDays(-1), 23)));
        var steps = new List<DatePipelineStep>();
        await h.Pipeline.RunAsync(Day, true, steps.Add, default);
        h.Calls.Should().Equal("standings", "fixtures", "historical_depth", "odds", "predict:1", "ai", "predict:1", "board", "publish");
        h.Fixtures.Verify(x => x.SyncLeagueForDateAsync(39, Day, It.IsAny<CancellationToken>()), Times.Once);
        h.Ai.Verify(x => x.SyncDateAsync(Day, true, It.IsAny<CancellationToken>()), Times.Once);
        h.Picks.Verify(x => x.GetBoardAsync(Day, "en", It.IsAny<CancellationToken>()), Times.Once);
        steps.Where(s => s.State == "completed").Select(s => s.Name).Should().Equal(DateSyncPipeline.StepNames);
        ((DatePredictionReport)steps.Last(s => s.Name == "ml_predictions").Report!).ModelVersions.Should().Equal("accepted-test-model");
    }

    [Fact]
    public async Task AiFailuresRetainFixtureIdsAndPreventPublishingAModelOnlyBoard()
    {
        using var h = new Harness(Match(1, At(Day)));
        h.Ai.Setup(x => x.SyncDateAsync(Day, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiSyncReport { Candidates = 1, Failed = 1, FailedFixtureIds = [1] });
        var steps = new List<DatePipelineStep>();
        await FluentActions.Awaiting(() => h.Pipeline.RunAsync(Day, false, steps.Add, default)).Should().ThrowAsync<InvalidOperationException>();
        var failed = steps.Last();
        failed.Name.Should().Be("ai_analysis"); failed.State.Should().Be("failed");
        ((AiSyncReport)failed.Report!).FailedFixtureIds.Should().Equal(1);
        h.Picks.Verify(x => x.GetBoardAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Ledger.Verify(x => x.RecordAsync(It.IsAny<DailyPickBoard>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingPredictionIsReportedRatherThanCountedAsSuccessfullyAnalyzed()
    {
        using var h = new Harness(Match(1, At(Day)));
        h.Precompute.Setup(x => x.RecomputeFixtureAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, MatchAnalysis>());
        var steps = new List<DatePipelineStep>();
        await FluentActions.Awaiting(() => h.Pipeline.RunAsync(Day, false, steps.Add, default)).Should().ThrowAsync<InvalidOperationException>();
        var report = (DatePredictionReport)steps.Last().Report!;
        report.Recomputed.Should().Be(0); report.FailedFixtureIds.Should().Equal(1);
        h.Ai.Verify(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TodayAlreadyStartedMatchesAreNotRecomputed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var h = new Harness(Match(1, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await h.Pipeline.RunAsync(today, false, _ => { }, default);
        h.Precompute.Verify(x => x.RecomputeFixtureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFixtureStartingDuringAiIsExcludedFromFinalRecompute()
    {
        using var h = new Harness(Match(1, At(Day)));
        h.Ai.Setup(x => x.SyncDateAsync(Day, false, It.IsAny<CancellationToken>())).Returns(async () =>
        {
            using var scope = h.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            (await db.Fixtures.SingleAsync()).Status = "1H";
            await db.SaveChangesAsync(default);
            return new AiSyncReport { Candidates = 1, Generated = 1 };
        });
        await h.Pipeline.RunAsync(Day, false, _ => { }, default);
        h.Precompute.Verify(x => x.RecomputeFixtureAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DataFailureStopsPaidAiAndPublication()
    {
        using var h = new Harness();
        h.Fixtures.Setup(x => x.SyncLeagueForDateAsync(39, Day, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        await FluentActions.Awaiting(() => h.Pipeline.RunAsync(Day, true, _ => { }, default)).Should().ThrowAsync<InvalidOperationException>();
        h.Ai.Verify(x => x.SyncDateAsync(It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Ledger.Verify(x => x.RecordAsync(It.IsAny<DailyPickBoard>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
