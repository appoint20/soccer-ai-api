using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Calibration;
using SoccerAi.Application.Services.Evaluation;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

public class IsotonicRegressionTests
{
    [Fact]
    public void Fit_OverconfidentPredictions_PulledTowardObservedRate()
    {
        // Model says 80% but hits only ~50%
        var samples = Enumerable.Range(0, 200)
            .Select(i => (0.80 + (i % 10) * 0.001, i % 2 == 0))
            .ToList();

        var model = IsotonicRegression.Fit(samples.Select(s => (s.Item1, s.Item2)).ToList());

        model.Predict(0.80).Should().BeApproximately(0.50, 0.05,
            "isotonic maps overconfident predictions down to observed frequency");
    }

    [Fact]
    public void Predict_IsMonotonicallyNonDecreasing()
    {
        var rng = new Random(7);
        var samples = Enumerable.Range(0, 500)
            .Select(_ =>
            {
                var p = rng.NextDouble();
                return (p, rng.NextDouble() < p); // roughly calibrated data
            })
            .ToList();

        var model = IsotonicRegression.Fit(samples.Select(s => (s.p, s.Item2)).ToList());

        double last = 0;
        for (var p = 0.0; p <= 1.0; p += 0.01)
        {
            var value = model.Predict(p);
            value.Should().BeGreaterThanOrEqualTo(last - 1e-12, "isotonic must never decrease");
            last = value;
        }
    }

    [Fact]
    public void Predict_ClampsAwayFromSaturation()
    {
        var allLose = Enumerable.Range(0, 100).Select(i => (0.2 + i * 0.001, false)).ToList();
        var model = IsotonicRegression.Fit(allLose.Select(s => (s.Item1, s.Item2)).ToList());

        model.Predict(0.25).Should().BeGreaterThanOrEqualTo(0.01,
            "EV math and log loss must never see 0");
    }

    [Fact]
    public void Fit_KnownPavExample_PoolsViolators()
    {
        // (0.1,F) (0.2,T) (0.3,F) (0.4,T): the 0.2T/0.3F violation pools to 0.5
        var model = IsotonicRegression.Fit([(0.1, false), (0.2, true), (0.3, false), (0.4, true)]);

        model.Predict(0.25).Should().BeApproximately(0.5, 1e-9);
        model.Predict(0.05).Should().BeApproximately(0.01, 1e-9, "leading all-false block clamps to floor");
        model.Predict(0.9).Should().BeApproximately(0.99, 1e-9, "trailing all-true block clamps to ceiling");
    }
}

public class ProbabilityCalibrationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ProbabilityCalibrationService _sut;
    private static readonly DateTimeOffset AsOf = new(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);

    public ProbabilityCalibrationServiceTests()
    {
        ProbabilityCalibrationService.ClearCache(); // static weekly cache — isolate tests

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _sut = new ProbabilityCalibrationService(
            _db,
            Microsoft.Extensions.Options.Options.Create(new CalibrationOptions()),
            new Mock<ILogger<ProbabilityCalibrationService>>().Object);
    }

    public void Dispose() => ProbabilityCalibrationService.ClearCache();

    private static WeightedPrediction Raw() => new()
    {
        HomeProb = 0.50, DrawProb = 0.25, AwayProb = 0.25,
        Over25Prob = 0.80, BTTSProb = 0.55, TwoToThreeGoalsProb = 0.45,
        Confidence = 0.50, MatchWinner = "home"
    };

    /// <summary>Seed N finished fixtures with over25 predicted 0.80 hitting only ~50%.</summary>
    private async Task SeedOverconfidentOver25Async(int count, DateTimeOffset newest)
    {
        for (var i = 0; i < count; i++)
        {
            var over = i % 2 == 0; // 50% hit rate
            var fixtureId = 1000 + i;
            _db.Fixtures.Add(new Fixture
            {
                Id = fixtureId, LeagueId = 39, HomeTeamId = 1, AwayTeamId = 2,
                Status = "FT", Date = newest.AddDays(-1 - i / 5.0),
                HomeGoal = over ? 2 : 1, AwayGoal = over ? 2 : 0
            });
            _db.FixtureAnalyses.Add(new FixtureAnalysis
            {
                FixtureId = fixtureId, Lang = "en",
                HomeProb = 0.5, DrawProb = 0.25, AwayProb = 0.25,
                Over25Prob = 0.80, BttsProb = 0.5, Goals23Prob = 0.4
            });
        }
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task BelowMinSamples_PassesThrough()
    {
        // Newest row a full week back: ALL 50 land strictly before the as-of
        // ISO week start (rows inside the week would be walk-forward-excluded).
        await SeedOverconfidentOver25Async(50, AsOf.AddDays(-7)); // < 300

        var result = await _sut.ApplyAsync(Raw(), AsOf);

        result.Calibrated.Over25Prob.Should().Be(0.80, "50 samples are not enough to activate");
        result.Trace.Should().Contain(t => t.Market == "over25" && !t.Active && t.TrainingSamples == 50);
    }

    [Fact]
    public async Task WithEnoughHistory_OverconfidentMarketPulledDown()
    {
        await SeedOverconfidentOver25Async(400, AsOf.AddDays(-7));

        var result = await _sut.ApplyAsync(Raw(), AsOf);

        result.Calibrated.Over25Prob.Should().BeApproximately(0.50, 0.06,
            "0.80 predictions hit ~50% historically");
        result.Trace.Should().Contain(t => t.Market == "over25" && t.Active && t.TrainingSamples == 400);
        result.Trace.Should().Contain(t => t.Market == "over25" && t.RawP == 0.80);
    }

    [Fact]
    public async Task WalkForward_TrainingDataFromOwnWeekOrLater_IsIgnored()
    {
        // All outcomes dated ON/AFTER the as-of week start — none may train the map.
        // (400 rows spread over ~80 days: newest at +90d keeps the oldest at +10d.)
        var weekStart = ProbabilityCalibrationService.IsoWeekStartUtc(AsOf);
        await SeedOverconfidentOver25Async(400, AsOf.AddDays(90));
        (await _db.Fixtures.MinAsync(f => f.Date)).Should().BeOnOrAfter(new DateTimeOffset(weekStart, TimeSpan.Zero));

        var result = await _sut.ApplyAsync(Raw(), AsOf);

        result.Calibrated.Over25Prob.Should().Be(0.80,
            "strict walk-forward: week k calibrates only on weeks < k");
    }

    [Fact]
    public async Task Calibrated1X2_RemainsADistribution()
    {
        await SeedOverconfidentOver25Async(400, AsOf.AddDays(-7));

        var result = await _sut.ApplyAsync(Raw(), AsOf);
        var p = result.Calibrated;

        // 1e-3 tolerance: components are rounded to 4 decimals after renormalization
        (p.HomeProb + p.DrawProb + p.AwayProb).Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    public async Task Disabled_IsAlwaysPassThrough()
    {
        ProbabilityCalibrationService.ClearCache();
        var sut = new ProbabilityCalibrationService(
            _db,
            Microsoft.Extensions.Options.Options.Create(new CalibrationOptions { IsotonicEnabled = false }),
            new Mock<ILogger<ProbabilityCalibrationService>>().Object);
        await SeedOverconfidentOver25Async(400, AsOf.AddDays(-7));

        var result = await sut.ApplyAsync(Raw(), AsOf);

        result.Calibrated.Over25Prob.Should().Be(0.80);
    }
}
