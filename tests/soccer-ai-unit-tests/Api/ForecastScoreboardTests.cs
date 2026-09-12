using FluentAssertions;
using Mediator.Net.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Forecasts;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Application.Services.Statistics;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The scoreboard decides which forecaster the product trusts, so its arithmetic
/// is pinned here rather than eyeballed in production.
/// </summary>
public class ForecastScoreboardTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly GetForecastScoreboardHandler _sut;

    private static readonly DateTimeOffset Kickoff = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    public ForecastScoreboardTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _sut = new GetForecastScoreboardHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<GetForecastScoreboardResponse> RunAsync(GetForecastScoreboardQuery? q = null)
    {
        var ctx = new Mock<IReceiveContext<GetForecastScoreboardQuery>>();
        ctx.SetupGet(c => c.Message).Returns(q ?? new GetForecastScoreboardQuery());
        return await _sut.Handle(ctx.Object, CancellationToken.None);
    }

    private async Task SeedAsync(
        int fixtureId, string model,
        double modelOver25, double systemOver25,
        int homeGoals, int awayGoals,
        double modelExpectedGoals = 2.5, bool settled = true)
    {
        if (!await _db.Fixtures.AnyAsync(f => f.Id == fixtureId))
            _db.Fixtures.Add(new Fixture { Id = fixtureId, Date = Kickoff, Status = settled ? "FT" : "NS",
                HomeGoal = homeGoals, AwayGoal = awayGoals });
        _db.ModelForecasts.Add(new ModelForecast
        {
            FixtureId = fixtureId,
            Model = model,
            KickoffUtc = Kickoff,
            PredictedAtUtc = Kickoff.AddDays(-1),
            ExpectedGoals = modelExpectedGoals,
            Over25Probability = modelOver25,
            BttsProbability = modelOver25,
            SystemExpectedGoals = 2.5,
            SystemOver25Probability = systemOver25,
            SystemBttsProbability = systemOver25,
            ActualHomeGoals = settled ? homeGoals : null,
            ActualAwayGoals = settled ? awayGoals : null,
            SettledAtUtc = settled ? Kickoff.AddHours(2) : null,
        });
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static ForecastMarketScoreDto Over25(
        GetForecastScoreboardResponse response, string forecaster) =>
        response.Forecasters
            .Single(f => f.Forecaster == forecaster)
            .Markets.Single(m => m.Market == "over_2_5");

    [Fact]
    public async Task Empty_ledger_reports_nothing_rather_than_a_verdict()
    {
        var result = await RunAsync();

        result.SettledFixtures.Should().Be(0);
        result.Forecasters.Should().BeEmpty();
        result.Leader.Should().BeNull();
    }

    [Fact]
    public async Task Unsettled_forecasts_are_excluded()
    {
        await SeedAsync(1, "m", 0.9, 0.5, 3, 0, settled: false);

        (await RunAsync()).SettledFixtures.Should().Be(0);
    }

    /// <summary>
    /// A perfect forecast scores 0; the maximally wrong one scores 1. This is
    /// the anchor the whole ranking rests on.
    /// </summary>
    [Fact]
    public async Task Brier_is_zero_when_certain_and_right_and_one_when_certain_and_wrong()
    {
        await SeedAsync(1, "right", modelOver25: 1.0, systemOver25: 0.5, homeGoals: 3, awayGoals: 0);
        await SeedAsync(1, "wrong", modelOver25: 0.0, systemOver25: 0.5, homeGoals: 3, awayGoals: 0);

        var result = await RunAsync();

        Over25(result, "right").BrierScore.Should().Be(0.0);
        Over25(result, "wrong").BrierScore.Should().Be(1.0);
        Over25(result, "system").BrierScore.Should().Be(0.25);
    }

    [Fact]
    public async Task Hedging_at_one_half_always_scores_a_quarter()
    {
        await SeedAsync(1, "hedger", 0.5, 0.5, 3, 0);
        await SeedAsync(2, "hedger", 0.5, 0.5, 0, 0);

        Over25(await RunAsync(), "hedger").BrierScore.Should().Be(0.25);
    }

    /// <summary>
    /// Hit rate treats 0.51 and 0.99 alike; Brier does not. This is exactly why
    /// the endpoint ranks on Brier and shows hit rate only for display.
    /// </summary>
    [Fact]
    public async Task Two_forecasters_can_share_a_hit_rate_and_differ_on_brier()
    {
        await SeedAsync(1, "timid", 0.51, 0.99, 3, 0);
        await SeedAsync(2, "timid", 0.51, 0.99, 4, 1);

        var result = await RunAsync();
        var timid = Over25(result, "timid");
        var bold = Over25(result, "system");

        timid.HitRate.Should().Be(bold.HitRate).And.Be(1.0);
        bold.BrierScore.Should().BeLessThan(timid.BrierScore);
    }

    [Fact]
    public async Task Base_rate_and_mean_probability_expose_a_hedger()
    {
        await SeedAsync(1, "m", 0.5, 0.5, 3, 0);
        await SeedAsync(2, "m", 0.5, 0.5, 4, 0);

        var market = Over25(await RunAsync(), "m");

        market.BaseRate.Should().Be(1.0);
        market.MeanProbability.Should().Be(0.5);
    }

    [Fact]
    public async Task Goals_mae_measures_distance_from_the_real_total()
    {
        // Forecast 2.5 against a 4-goal game → error 1.5.
        await SeedAsync(1, "m", 0.5, 0.5, 3, 1, modelExpectedGoals: 2.5);

        (await RunAsync()).Forecasters
            .Single(f => f.Forecaster == "m").GoalsMae.Should().Be(1.5);
    }

    /// <summary>
    /// The system is one forecaster, not one per model. Counting its row once
    /// per model would weight fixtures by how many models happened to cover them.
    /// </summary>
    [Fact]
    public async Task System_is_counted_once_per_fixture_not_once_per_model()
    {
        await SeedAsync(1, "model-a", 0.6, 0.7, 3, 0);
        await SeedAsync(1, "model-b", 0.6, 0.7, 3, 0);

        var result = await RunAsync();

        result.SettledFixtures.Should().Be(1);
        result.Forecasters.Single(f => f.Forecaster == "system").SettledFixtures.Should().Be(1);
        result.Forecasters.Should().HaveCount(3); // system + two models
    }

    [Fact]
    public async Task No_leader_is_named_on_a_thin_sample()
    {
        await SeedAsync(1, "m", 1.0, 0.1, 3, 0);

        var result = await RunAsync();

        result.Forecasters.Should().OnlyContain(f => f.SampleTooSmall);
        result.Leader.Should().BeNull("a ranking on one fixture is noise");
    }

    [Fact]
    public async Task Leader_is_the_lowest_brier_once_the_sample_is_large_enough()
    {
        // 2-1: over 2.5 AND both teams scored, so a single probability is right
        // on both markets. A 3-0 would make the markets disagree and — since the
        // seed uses one probability for both — score the two forecasters
        // identically, which is a property of the fixture, not of the ranking.
        for (var i = 1; i <= 60; i++)
            await SeedAsync(i, "sharp", modelOver25: 0.9, systemOver25: 0.1, homeGoals: 2, awayGoals: 1);

        var result = await RunAsync();

        result.Forecasters.Should().OnlyContain(f => !f.SampleTooSmall);
        result.Leader.Should().Be("sharp");
    }

    [Fact]
    public async Task Date_range_filters_on_kickoff()
    {
        await SeedAsync(1, "m", 0.9, 0.5, 3, 0);

        var outside = await RunAsync(new GetForecastScoreboardQuery
        {
            From = new DateOnly(2026, 9, 1),
        });

        outside.SettledFixtures.Should().Be(0);
    }

    [Fact]
    public async Task Different_cohorts_do_not_create_a_false_global_leader()
    {
        for (var i = 1; i <= 60; i++)
        {
            await SeedAsync(i, "first", .9, .5, 2, 1);
            await SeedAsync(i + 60, "second", .6, .5, 2, 1);
        }
        var result = await RunAsync();
        result.Leader.Should().BeNull(); result.Forecasters.Should().BeEmpty();
        result.PairedComparisons!.Models.Should().HaveCount(2).And.OnlyContain(m => m.ComparableFixtures == 60);
    }

    [Fact]
    public async Task Hindsight_and_missing_statistical_inputs_are_excluded()
    {
        await SeedAsync(1, "m", .9, .5, 2, 1);
        await SeedAsync(2, "m", .9, 0, 2, 1);
        _db.ModelForecasts.Single(f => f.FixtureId == 1).PredictedAtUtc = Kickoff;
        await _db.SaveChangesAsync();
        var result = await RunAsync();
        result.Forecasters.Should().BeEmpty();
        var comparison = result.PairedComparisons!.Models.Single();
        comparison.InvalidTiming.Should().Be(1); comparison.MissingSystemInputs.Should().Be(1);
        comparison.Markets.Should().OnlyContain(m => m.Ai.Accuracy == null && m.System.Accuracy == null);
    }
}

public class RecordedAiForecastStatisticsTests
{
    [Fact]
    public void Paired_comparison_counts_switches_and_checks_actual_kickoff_and_duplicates()
    {
        var kickoff = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = Enumerable.Range(1, 7).Select(id => new Fixture { Id = id, Status = "FT", Date = kickoff, HomeGoal = 3, AwayGoal = 0 }).ToList();
        var rows = fixtures.Select(f => new ModelForecast { FixtureId = f.Id, Model = "m", KickoffUtc = kickoff,
            PredictedAtUtc = kickoff.AddHours(-1), SystemOver25Probability = .4, SystemBttsProbability = .4,
            Over25Probability = .7, BttsProbability = .7 }).ToList();
        rows[1].PredictedAtUtc = kickoff.AddMinutes(-59);
        rows[2].KickoffUtc = kickoff.AddHours(2);
        rows[3].SystemOver25Probability = 0;
        rows[4].BttsProbability = double.NaN;
        fixtures[5].Status = "PST";
        rows.Add(rows[6]);
        var report = RecordedAiForecastStatistics.Build(fixtures, rows).Models.Single();
        report.ComparableFixtures.Should().Be(1); report.InvalidTiming.Should().Be(2);
        report.MissingSystemInputs.Should().Be(1); report.InvalidAiInputs.Should().Be(1);
        report.UnfinishedOrMissingFixture.Should().Be(1); report.DuplicateRecords.Should().Be(2);
        var over = report.Markets.Single(m => m.Market == "over25");
        over.Ai.Correct.Should().Be(1); over.System.Correct.Should().Be(0);
        over.ImprovedCalls.Should().Be(1); over.WorsenedCalls.Should().Be(0);
        over.Ai.BrierScore.Should().BeApproximately(.09, 1e-12);
        over.BrierDifference.Should().BeApproximately(-.27, 1e-12);
        var btts = report.Markets.Single(m => m.Market == "btts");
        btts.WorsenedCalls.Should().Be(1); btts.Ai.Accuracy.Should().Be(0);
        RecordedAiForecastStatistics.Pair(fixtures, rows).Should().HaveCount(1);
    }
}

/// <summary>The ledger is the evidence base, so its write rules are pinned too.</summary>
public class ModelForecastLedgerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ModelForecastLedger _sut;

    public ModelForecastLedgerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _sut = new ModelForecastLedger(_db, new Mock<ILogger<ModelForecastLedger>>().Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task First_valid_forecast_is_frozen_and_kickoff_prevents_new_records()
    {
        var kickoff = DateTimeOffset.UtcNow.AddDays(1);
        var fixture = new Fixture { Id = 1, Date = kickoff, Status = "NS" };
        _db.Fixtures.Add(fixture); await _db.SaveChangesAsync();
        var analysis = new MatchAnalysis { Id = 1, Date = kickoff, Prediction = new PredictionResponse {
            Over25 = new BoolPrediction { Probability = .6 }, BTTS = new BoolPrediction { Probability = .55 } } };
        var forecast = new GoalsForecast { Model = "m", Over25Probability = .7, BttsProbability = .7, Confidence = .7,
            ExpectedGoals = 3, PredictedHomeGoals = 2, PredictedAwayGoals = 1, Rationale = "Recorded before kickoff" };
        await _sut.RecordAsync(analysis, [forecast]);
        await _sut.RecordAsync(analysis, [forecast with { Over25Probability = .9 }]);
        _db.ModelForecasts.Single().Over25Probability.Should().Be(.7);
        fixture.Date = DateTimeOffset.UtcNow.AddMinutes(-1); await _db.SaveChangesAsync();
        await _sut.RecordAsync(analysis, [forecast with { Model = "new" }]);
        _db.ModelForecasts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Only_finished_fixtures_are_settled()
    {
        _db.Fixtures.Add(new Fixture
        {
            Id = 1, ApiId = 1, HomeTeamId = 1, AwayTeamId = 2, LeagueId = 39,
            Date = DateTimeOffset.UtcNow, Status = "PST", HomeGoal = 0, AwayGoal = 0,
        });
        _db.ModelForecasts.Add(new ModelForecast { FixtureId = 1, Model = "m" });
        await _db.SaveChangesAsync(CancellationToken.None);

        var settled = await _sut.SettleAsync(CancellationToken.None);

        settled.Should().Be(0, "a postponed fixture's score would score every model against noise");
    }

    [Fact]
    public async Task Finished_fixtures_settle_with_their_score()
    {
        _db.Fixtures.Add(new Fixture
        {
            Id = 1, ApiId = 1, HomeTeamId = 1, AwayTeamId = 2, LeagueId = 39,
            Date = DateTimeOffset.UtcNow, Status = "FT", HomeGoal = 2, AwayGoal = 1,
        });
        _db.ModelForecasts.Add(new ModelForecast { FixtureId = 1, Model = "m" });
        await _db.SaveChangesAsync(CancellationToken.None);

        (await _sut.SettleAsync(CancellationToken.None)).Should().Be(1);

        var row = _db.ModelForecasts.Single();
        row.ActualTotalGoals.Should().Be(3);
        row.ActualOver25.Should().BeTrue();
        row.ActualBtts.Should().BeTrue();
        row.IsSettled.Should().BeTrue();
    }

    [Fact]
    public async Task A_goalless_draw_settles_as_under_and_no_btts()
    {
        _db.Fixtures.Add(new Fixture
        {
            Id = 1, ApiId = 1, HomeTeamId = 1, AwayTeamId = 2, LeagueId = 39,
            Date = DateTimeOffset.UtcNow, Status = "FT", HomeGoal = 0, AwayGoal = 0,
        });
        _db.ModelForecasts.Add(new ModelForecast { FixtureId = 1, Model = "m" });
        await _db.SaveChangesAsync(CancellationToken.None);

        await _sut.SettleAsync(CancellationToken.None);

        var row = _db.ModelForecasts.Single();
        row.ActualOver25.Should().BeFalse();
        row.ActualBtts.Should().BeFalse();
    }
}
