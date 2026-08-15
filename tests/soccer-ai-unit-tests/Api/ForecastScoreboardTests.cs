using FluentAssertions;
using Mediator.Net.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Forecasts;
using SoccerAi.Application.Services.Forecasts;
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
        await SeedAsync(1, "right", modelOver25: 1.0, systemOver25: 0.0, homeGoals: 3, awayGoals: 0);
        await SeedAsync(1, "wrong", modelOver25: 0.0, systemOver25: 0.0, homeGoals: 3, awayGoals: 0);

        var result = await RunAsync();

        Over25(result, "right").BrierScore.Should().Be(0.0);
        Over25(result, "wrong").BrierScore.Should().Be(1.0);
        // The system said 0.0 on an over — same as the wrong model.
        Over25(result, "system").BrierScore.Should().Be(1.0);
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
        await SeedAsync(1, "m", 1.0, 0.0, 3, 0);

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
