using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Application.Services.Statistics;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class CombinedPredictionTests
{
    private static readonly string[] Names = ["historical", "ml", "provider", "ai"];

    [Fact]
    public void FourSourcesUseConfiguredWeightsAndRetainAllMarkets()
    {
        var sources = Names.Select((name, index) => new PredictionSource(name, "test",
            CombinedMarkets.From(Probabilities(1 + index * .3, 1.1)))).ToArray();
        var service = new CombinedPredictionService(Options.Create(new CombinedPredictionOptions()));
        var now = DateTimeOffset.UtcNow;
        var result = service.Combine(1, now.AddDays(1), now, sources);
        result.AllFourSourcesAvailable.Should().BeTrue();
        result.Sources.Select(source => source.GoalsWeight).Should().Equal(.45, .30, .15, .10);
        result.Markets.Btts.Should().BeApproximately(sources[0].Markets!.Btts!.Value * .45
            + sources[1].Markets!.Btts!.Value * .30 + sources[2].Markets!.Btts!.Value * .15
            + sources[3].Markets!.Btts!.Value * .10, 1e-12);
        (result.Markets.HomeWin + result.Markets.Draw + result.Markets.AwayWin).Should().BeApproximately(1, 1e-12);
        result.Markets.HasGoals.Should().BeTrue();
        result.Markets.ToPrediction().TwoToThreeGoalsProb.Should().Be(result.Markets.TwoToThreeGoals);
    }

    [Fact]
    public void MissingOrInvalidSourcesAreExcludedNotReplacedWithZeroProbability()
    {
        var history = CombinedMarkets.From(Probabilities(1.6, 1.1));
        var sources = new[]
        {
            new PredictionSource("historical", "test", history), new PredictionSource("ml", "test"),
            new PredictionSource("provider", "test", new CombinedMarkets { HomeWin = .2, Draw = .6, AwayWin = .2 }),
            new PredictionSource("ai", "test", history with { Btts = 65, HomeWin = double.NaN })
        };
        var result = new CombinedPredictionService(Options.Create(new CombinedPredictionOptions()))
            .Combine(1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow, sources);
        result.Status.Should().Be("partial");
        result.Markets.Btts.Should().Be(history.Btts);
        result.Sources.Single(source => source.Name == "historical").GoalsWeight.Should().Be(1);
        result.Sources.Single(source => source.Name == "provider").OutcomeWeight.Should().BeApproximately(.25, 1e-12);
        result.Sources.Single(source => source.Name == "ai").GoalsWeight.Should().Be(0);
    }

    [Fact]
    public void ProviderRatingsAreNotTreatedAsGoalProbabilities()
    {
        var provider = new ProviderPrediction { PercentHome = .5, PercentDraw = .3, PercentAway = .2,
            Attack = .99, Defence = .99, Goals = .99, Poisson = .99, Total = .99 };
        var missing = CombinedPredictionService.ProviderMarkets(provider)!;
        missing.HasOutcomes.Should().BeTrue();
        missing.HasGoals.Should().BeFalse();
        var derived = CombinedPredictionService.ProviderMarkets(provider with
        {
            Home = new TeamRecentForm { Played = 5, GoalsForAverage = 1.5, GoalsAgainstAverage = 1 },
            Away = new TeamRecentForm { Played = 5, GoalsForAverage = 1, GoalsAgainstAverage = 1.5 }
        })!;
        derived.HasGoals.Should().BeTrue();
        derived.Btts.Should().BeApproximately((1 - Math.Exp(-1.5)) * (1 - Math.Exp(-1)), 1e-7);
    }

    [Fact]
    public void FutureEvidenceAndInvalidWeightsCannotInfluencePrediction()
    {
        var now = DateTimeOffset.UtcNow;
        var sources = Names.Select(name => new PredictionSource(name, "test", CombinedMarkets.From(Probabilities(1, 1)),
            CapturedAtUtc: name == "ai" ? now.AddHours(1) : now)).ToArray();
        var result = new CombinedPredictionService(Options.Create(new CombinedPredictionOptions()))
            .Combine(1, now.AddDays(1), now, sources);
        result.Sources.Last().GoalsWeight.Should().Be(0);
        var invalid = new CombinedPredictionService(Options.Create(new CombinedPredictionOptions { AiWeight = -1 }));
        var run = () => invalid.Combine(1, now.AddDays(1), now, sources);
        run.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task RefreshPersistsNewVersionsAndBothLanguageSnapshotsWithoutReplacingTheLedger()
    {
        await using var harness = await Harness.Create();
        var first = await harness.Service.RefreshAsync(1, refreshNarration: false);
        first.Prediction.AllFourSourcesAvailable.Should().BeTrue();
        harness.Forecasts.Verify(service => service.ForecastCombinedAsync(It.Is<MatchAnalysis>(analysis =>
            analysis.Provider != null && analysis.CombinedPrediction!.Sources.Count == 4 && analysis.Prediction!.Draw.Probability > 0),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        first.Prediction.Sources.Single(source => source.Name == "ml").Markets!.HomeWin
            .Should().Be(Probabilities(.8, 1.8).HomeWin);
        var frozenLedger = (await harness.Db.PredictionSnapshots.SingleAsync()).ContextJson;
        var firstPayload = (await harness.Db.CombinedPredictionSnapshots.SingleAsync()).PredictionJson;
        var second = await harness.Service.RefreshAsync(1, refreshNarration: false);
        second.SnapshotId.Should().NotBe(first.SnapshotId);
        (await harness.Db.CombinedPredictionSnapshots.CountAsync()).Should().Be(2);
        (await harness.Db.CombinedPredictionSnapshots.FindAsync(first.SnapshotId))!.PredictionJson.Should().Be(firstPayload);
        (await harness.Db.PredictionSnapshots.SingleAsync()).ContextJson.Should().Be(frozenLedger);
        foreach (var row in await harness.Db.FixtureAnalyses.ToListAsync())
        {
            var response = AnalysisSnapshotSerializer.Deserialize(row.SnapshotJson)!;
            response.CombinedPrediction!.CapturedAtUtc.Should().Be(second.Prediction.CapturedAtUtc);
            response.Prediction!.BTTS.Probability.Should().BeApproximately(second.Prediction.Markets.Btts!.Value, .0001);
            response.BttsAndOver25Probability.Should().Be(second.Prediction.Markets.BttsAndOver25);
            response.MatchPrediction!.Prediction.Should().Be(second.Prediction.Markets.ToPrediction().MatchWinner);
            response.Prediction.TwoToThreeGoals.Probability.Should().BeGreaterThan(0);
        }
        (await harness.Db.FixtureAnalyses.CountAsync()).Should().Be(2);
        harness.Calibration.Verify(service => service.ApplyAsync(It.IsAny<WeightedPrediction>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MissingAiIsReportedAndNarrationFailureDoesNotLoseSavedPrediction()
    {
        await using var harness = await Harness.Create();
        harness.Forecasts.Setup(service => service.ForecastCombinedAsync(It.IsAny<MatchAnalysis>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((GoalsForecast?)null);
        harness.Narration.Setup(service => service.SyncSingleFixtureAsync(1, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));
        var result = await harness.Service.RefreshAsync(1);
        result.Prediction.AllFourSourcesAvailable.Should().BeFalse();
        result.NarrationRefreshed.Should().BeFalse();
        result.Warnings.Should().Contain(warning => warning.Contains("narration failed"));
        (await harness.Db.CombinedPredictionSnapshots.CountAsync()).Should().Be(1);
        harness.Narration.Verify(service => service.SyncSingleFixtureAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FreshBilingualNarrationReceivesTheFinalBlendAndIsSavedForBothLanguages()
    {
        await using var harness = await Harness.Create();
        var narrator = new Mock<IAiAnalysisService>();
        narrator.Setup(service => service.AnalyzeBatchAsync(It.Is<List<AiBatchItem>>(items =>
                items.Count == 1 && items[0].CombinedPrediction!.AllFourSourcesAvailable
                && items[0].ModelDraw == items[0].CombinedPrediction!.Markets.Draw), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Dictionary<int, AiBilingualResult>
            {
                [1] = new()
                {
                    FixtureId = 1, Confidence = 70, OverallConfidence = 70,
                    GeneratedAtUtc = DateTimeOffset.UtcNow, ModelVersion = "nvidia/test:free",
                    En = new() { Analysis = "Fresh English narration of the combined prediction." },
                    De = new() { Analysis = "Neue deutsche Analyse der kombinierten Prognose." }
                }
            });
        var sync = new AiSyncService(harness.Db, harness.Analysis, narrator.Object, harness.Precompute,
            Mock.Of<ILeagueTierService>(), NullLogger<AiSyncService>.Instance);
        harness.Narration.Setup(service => service.SyncSingleFixtureAsync(1, true, It.IsAny<CancellationToken>()))
            .Returns(() => sync.SyncSingleFixtureAsync(1, true));
        var result = await harness.Service.RefreshAsync(1);
        result.NarrationRefreshed.Should().BeTrue();
        var rows = await harness.Db.FixtureAnalyses.OrderBy(row => row.Lang).ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(row => row.AiModelVersion == "nvidia/test:free"
            && row.AiGeneratedAtUtc >= result.Prediction.CapturedAtUtc);
        foreach (var row in rows)
        {
            var snapshot = AnalysisSnapshotSerializer.Deserialize(row.SnapshotJson)!;
            snapshot.Ai!.Analysis.Should().Be(row.Analysis);
            snapshot.CombinedPrediction!.CapturedAtUtc.Should().Be(result.Prediction.CapturedAtUtc);
            AiNarrativeIntegrity.NeedsSnapshotRefresh(snapshot, row).Should().BeFalse();
        }
        narrator.Verify(service => service.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFixtureStartingDuringTheAiCallCannotAcquireAPreMatchPrediction()
    {
        await using var harness = await Harness.Create();
        harness.Forecasts.Setup(service => service.ForecastCombinedAsync(It.IsAny<MatchAnalysis>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).Returns(async () =>
        {
            var fixture = await harness.Db.Fixtures.SingleAsync();
            fixture.Status = "1H";
            await harness.Db.SaveChangesAsync();
            return null;
        });
        var run = () => harness.Service.RefreshAsync(1, refreshNarration: false);
        await run.Should().ThrowAsync<InvalidOperationException>();
        (await harness.Db.CombinedPredictionSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void WholeMatchPredictionCanBeDrawAndStaleNarrationDoesNotCauseAnEndlessRefresh()
    {
        var markets = CombinedMarkets.From(Probabilities(1, 1)) with { HomeWin = .2, Draw = .6, AwayWin = .2 };
        var probabilities = markets.ToPrediction();
        var response = new MatchAnalysis
        {
            Prediction = new PredictionResponse { MatchWinner = new StringPrediction { Prediction = probabilities.MatchWinner } },
            CombinedPrediction = new CombinedPrediction(1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow, markets, [])
        };
        response.MatchPrediction!.Prediction.Should().Be("draw");
        AiNarrativeIntegrity.NeedsSnapshotRefresh(response, new FixtureAnalysis { Analysis = "Old narration" }).Should().BeFalse();
        JsonSerializer.Serialize(response).Should().Contain("match_prediction").And.Contain("two_to_three_goals");
    }

    [Fact]
    public void DrawQualificationUsesTheDrawGateRatherThanTheHomeAwayGate()
    {
        var result = new FixtureAnalysisResult
        {
            FixtureId = 1, TeamStats = new TeamStatsResponse(), Models = new StatisticalModels(),
            H2H = new HeadToHeadModel(), LeagueName = "Test",
            Prediction = (CombinedMarkets.From(Probabilities(1, 1)) with { HomeWin = .2, Draw = .6, AwayWin = .2 }).ToPrediction(),
            Decisions = new DecisionServiceResult { Markets = new QualificationDecisions
            {
                Draw = new DrawDecision { IsQualified = true, Label = "Draw qualified" }
            } }
        };
        var response = AnalysisResponseMapper.MapToResponse(new Fixture { Date = DateTimeOffset.UtcNow.AddDays(1) },
            result, new Team { Name = "Home" }, new Team { Name = "Away" }, null);
        response.Prediction!.Draw.IsQualified.Should().BeTrue();
        response.MatchPrediction!.IsQualified.Should().BeTrue();
        response.MatchPrediction.Reason.Should().Be("Draw qualified");
        response.Prediction.HomeWin.IsQualified.Should().BeFalse();
        response.Prediction.AwayWin.IsQualified.Should().BeFalse();
    }

    private static PoissonProbabilities Probabilities(double home, double away)
    {
        var markets = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(home, away, 0, 15));
        return new PoissonProbabilities
        {
            HomeWin = markets.HomeWin, Draw = markets.Draw, AwayWin = markets.AwayWin,
            Over25 = markets.Over25, BothTeamScoredGoal = markets.Btts, TwoToThreeGoals = markets.TwoToThreeGoals,
            BttsAndOver25 = markets.BttsAndOver25, HomeExpectedGoals = home, AwayExpectedGoals = away
        };
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Mock<IMatchForecastService> Forecasts { get; } = new();
        public Mock<IAiSyncService> Narration { get; } = new();
        public Mock<IProbabilityCalibrationService> Calibration { get; } = new();
        public CombinedPredictionRefreshService Service { get; private set; } = null!;
        public MatchAnalysisService Analysis { get; private set; } = null!;
        public AnalysisPrecomputeService Precompute { get; private set; } = null!;

        public static async Task<Harness> Create()
        {
            var harness = new Harness();
            var fixture = new Fixture { Id = 1, ApiId = 77, HomeTeamId = 10, AwayTeamId = 20,
                Date = DateTimeOffset.UtcNow.AddDays(2), Status = "NS" };
            harness.Db.Fixtures.Add(fixture);
            harness.Db.Teams.AddRange(new Team { ApiId = 10, Name = "Home" }, new Team { ApiId = 20, Name = "Away" });
            await harness.Db.SaveChangesAsync();
            var data = new Mock<IMatchDataProvider>();
            data.Setup(service => service.LoadAsync(It.IsAny<Fixture>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchData { TeamStats = new TeamStatsResponse(), H2H = new HeadToHeadModel() });
            var history = Probabilities(1.7, 1.1);
            var historical = new Mock<IDixonColesModel>();
            historical.Setup(service => service.CalculateProbabilitiesAsync(It.IsAny<int>(), 10, 20,
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(history);
            var ml = new Mock<IGoalRateForecaster>();
            ml.Setup(service => service.ForecastAsync(It.IsAny<Fixture>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GoalRateForecast(1.7, 1.1, history, "ml-test") { RawMlProbabilities = Probabilities(.8, 1.8) });
            var provider = new Mock<IApiFootballService>();
            provider.Setup(service => service.GetPredictionAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderPrediction
            {
                PercentHome = .35, PercentDraw = .3, PercentAway = .35,
                Home = new TeamRecentForm { Played = 5, GoalsForAverage = 1.5, GoalsAgainstAverage = 1.2 },
                Away = new TeamRecentForm { Played = 5, GoalsForAverage = 1.2, GoalsAgainstAverage = 1.5 }
            });
            harness.Forecasts.Setup(service => service.ForecastCombinedAsync(It.IsAny<MatchAnalysis>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new GoalsForecast
            {
                Model = "nvidia/test:free", ExpectedGoals = 2.7, PredictedHomeGoals = 1, PredictedAwayGoals = 1,
                BttsProbability = .6, Over25Probability = .55, Confidence = .6, Rationale = "Test evidence",
                Markets = CombinedMarkets.From(Probabilities(1.3, 1.4)), RawResponseJson = "{\"test\":true}"
            });
            var pipeline = new Mock<IProbabilityPipeline>();
            pipeline.Setup(service => service.RunAsync(It.IsAny<Fixture>(), It.IsAny<TeamStatsResponse>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProbabilityBundle { Poisson = new PoissonModel { IsValid = true },
                    Calibrated = new CalibratedProbabilities { HomeWin = .5, Draw = .3, AwayWin = .2, Btts = .6, Over25 = .55, TwoToThreeGoals = .45 } });
            var decisions = new Mock<IDecisionService>();
            decisions.Setup(service => service.Evaluate(It.IsAny<MatchContext>(), It.IsAny<TeamStatsResponse>(),
                It.IsAny<HeadToHeadModel>(), It.IsAny<WeightedPrediction>(), It.IsAny<StatisticalModels>(),
                It.IsAny<StrategicSignals>(), It.IsAny<AiAnalysisDto>())).ReturnsAsync(new DecisionServiceResult());
            var analysis = new MatchAnalysisService(data.Object, pipeline.Object, decisions.Object,
                Mock.Of<IStrategicSignalService>(), harness.Calibration.Object, harness.Db);
            var precompute = new AnalysisPrecomputeService(harness.Db, analysis, Mock.Of<ILeagueTierService>(),
                new PredictionLedger(harness.Db), NullLogger<AnalysisPrecomputeService>.Instance);
            harness.Analysis = analysis;
            harness.Precompute = precompute;
            var options = Options.Create(new CombinedPredictionOptions());
            harness.Service = new CombinedPredictionRefreshService(harness.Db, historical.Object, ml.Object, provider.Object,
                harness.Forecasts.Object, data.Object, new CombinedPredictionService(options), precompute, harness.Narration.Object,
                options, NullLogger<CombinedPredictionRefreshService>.Instance);
            return harness;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
