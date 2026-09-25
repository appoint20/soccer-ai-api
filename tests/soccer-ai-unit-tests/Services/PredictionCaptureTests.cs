using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The provider's prediction is the one match evidence that needs no
/// bookmaker, so it has to arrive without spending the budget it competes for:
/// twice over a fixture's life, and never for a division that has none.
/// </summary>
public class PredictionCaptureTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IApiFootballService> _api = new();

    private FixtureSyncService Sync(bool quotaCritical = false)
    {
        var tiers = new Mock<ILeagueTierService>();
        tiers.Setup(x => x.GetSyncLeagueIds()).Returns([39]);
        var quota = Mock.Of<IApiQuotaTracker>(x => x.IsDailyQuotaCritical == quotaCritical);
        return new(_api.Object, _db, tiers.Object, quota, Options.Create(new OddsSyncOptions()),
            Options.Create(new SyncOptions()), NullLogger<FixtureSyncService>.Instance);
    }

    private Fixture Upcoming(double hoursAhead = 72) => new()
    {
        Id = 1, ApiId = 1001, LeagueId = 39, Status = "NS",
        Date = Now.AddHours(hoursAhead), HomeTeamId = 10, AwayTeamId = 20
    };

    private static ProviderPrediction Prediction() => new()
    {
        PercentHome = .45, PercentDraw = .45, PercentAway = .10,
        Form = .63, Attack = .53, Defence = .80, HeadToHead = .85, Total = .712,
        Advice = "Double chance", WinnerName = "Home",
        Home = new TeamRecentForm { Played = 4, Form = 1.0, GoalsForAverage = 2.3, GoalsAgainstAverage = 0.5 },
        Away = new TeamRecentForm { Played = 4, Form = .58, GoalsForAverage = 2.0, GoalsAgainstAverage = 2.0 }
    };

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ThePredictionIsStoredAgainstTheFixture()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetPredictionAsync(1001, It.IsAny<CancellationToken>())).ReturnsAsync(Prediction());

        var captured = await Sync().CapturePredictionsAsync(CancellationToken.None);

        captured.Should().Be(1);
        var row = await _db.FixturePredictions.SingleAsync();
        row.PercentHome.Should().Be(.45);
        row.HeadToHead.Should().Be(.85);
        row.HomeGoalsFor.Should().Be(2.3);
        row.AwayGoalsAgainst.Should().Be(2.0);
        (await _db.Fixtures.SingleAsync()).PredictionCheckedAtUtc.Should().NotBeNull();
    }

    /// <summary>Outside the final window, one fetch is all a fixture gets.</summary>
    [Fact]
    public async Task AFixtureFarFromKickoffIsNotAskedAboutTwice()
    {
        _db.Fixtures.Add(Upcoming(hoursAhead: 72));
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetPredictionAsync(1001, It.IsAny<CancellationToken>())).ReturnsAsync(Prediction());

        await Sync().CapturePredictionsAsync(CancellationToken.None);
        var second = await Sync().CapturePredictionsAsync(CancellationToken.None);

        second.Should().Be(0);
        _api.Verify(x => x.GetPredictionAsync(1001, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Inside the last day the form it is built on has settled, so one refresh
    /// is worth its request — and the stored row is replaced, not duplicated.
    /// </summary>
    [Fact]
    public async Task AFixtureNearKickoffIsRefreshedOnceAndOverwritesItsRow()
    {
        var fixture = Upcoming(hoursAhead: 10);
        fixture.PredictionCheckedAtUtc = Now.AddDays(-2);
        _db.Fixtures.Add(fixture);
        _db.FixturePredictions.Add(new FixturePrediction { FixtureId = 1, PercentHome = .20 });
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetPredictionAsync(1001, It.IsAny<CancellationToken>())).ReturnsAsync(Prediction());

        await Sync().CapturePredictionsAsync(CancellationToken.None);

        _db.FixturePredictions.Should().ContainSingle();
        (await _db.FixturePredictions.SingleAsync()).PercentHome.Should().Be(.45);
    }

    /// <summary>A division the provider does not cover must not be re-asked forever.</summary>
    [Fact]
    public async Task ANullAnswerStillCountsAsChecked()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetPredictionAsync(1001, It.IsAny<CancellationToken>())).ReturnsAsync((ProviderPrediction?)null);

        await Sync().CapturePredictionsAsync(CancellationToken.None);

        (await _db.Fixtures.SingleAsync()).PredictionCheckedAtUtc.Should().NotBeNull();
        _db.FixturePredictions.Should().BeEmpty();
    }

    [Fact]
    public async Task ACriticalDailyBudgetStopsTheStepBeforeItSpends()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();

        var captured = await Sync(quotaCritical: true).CapturePredictionsAsync(CancellationToken.None);

        captured.Should().Be(0);
        _api.Verify(x => x.GetPredictionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
