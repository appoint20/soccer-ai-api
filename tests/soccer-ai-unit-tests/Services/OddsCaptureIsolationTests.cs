using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// One fixture whose odds cannot be read must not cost every other fixture its
/// price refresh. It did: the exception ended the capture run, and because that
/// fixture stayed due, it ended every following run as well.
/// </summary>
public class OddsCaptureIsolationTests
{
    // Unique, so the process-wide odds-coverage cache cannot answer from another test.
    private const int League = 990_431;

    private static (ApplicationDbContext Db, Mock<IApiFootballService> Api, FixtureSyncService Sut) Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        db.Fixtures.AddRange(
            new Fixture { Id = 1, ApiId = 101, LeagueId = League, Status = "NS", Date = now.AddHours(2) },
            new Fixture { Id = 2, ApiId = 102, LeagueId = League, Status = "NS", Date = now.AddHours(3) });
        db.SaveChanges();

        var api = new Mock<IApiFootballService>();
        api.Setup(a => a.HasOddsCoverageAsync(League, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        api.Setup(a => a.GetFixtureOddsQuotesAsync(102)).ReturnsAsync(new List<OddsQuote>
        {
            new("Bet365", OddsMarkets.HomeWin, 2.10, now.AddMinutes(-5)),
            new("Bet365", OddsMarkets.Draw, 3.40, now.AddMinutes(-5)),
            new("Bet365", OddsMarkets.AwayWin, 3.60, now.AddMinutes(-5)),
        });

        var tiers = new Mock<ILeagueTierService>();
        tiers.Setup(t => t.GetSyncLeagueIds()).Returns(new[] { League });

        var sut = new FixtureSyncService(api.Object, db, tiers.Object, Mock.Of<IApiQuotaTracker>(),
            Options.Create(new OddsSyncOptions()), Options.Create(new SyncOptions()),
            NullLogger<FixtureSyncService>.Instance);
        return (db, api, sut);
    }

    [Fact]
    public async Task AnUnreadableResponseSkipsThatFixtureAndTheRunContinues()
    {
        var (db, api, sut) = Build();
        api.Setup(a => a.GetFixtureOddsQuotesAsync(101)).ThrowsAsync(new ExternalApiException(
            "API-Football", "Odds response could not be read.",
            innerException: new InvalidOperationException("The target element has type 'Number'.")));

        var captured = await sut.CaptureUpcomingOddsAsync(CancellationToken.None);

        captured.Should().Be(1);
        (await db.Fixtures.SingleAsync(f => f.Id == 1)).OddsCheckedAtUtc.Should().BeNull();
        var priced = await db.Fixtures.SingleAsync(f => f.Id == 2);
        priced.OddsCheckedAtUtc.Should().NotBeNull();
        priced.HomeWinOdds.Should().Be(2.10);
        (await db.FixtureOddsQuotes.CountAsync(q => q.FixtureId == 2)).Should().Be(3);
    }

    /// <summary>
    /// Provider-level failures still stop the run: continuing through a rate
    /// limit or a rejected key would only repeat the same failure fixture by fixture.
    /// </summary>
    [Fact]
    public async Task AProviderRejectionStillStopsTheRun()
    {
        var (_, api, sut) = Build();
        api.Setup(a => a.GetFixtureOddsQuotesAsync(101)).ThrowsAsync(new ExternalApiException(
            "API-Football", "Provider rejected /odds (error categories: rateLimit); no sync data accepted."));

        var run = () => sut.CaptureUpcomingOddsAsync(CancellationToken.None);

        await run.Should().ThrowAsync<ExternalApiException>();
        api.Verify(a => a.GetFixtureOddsQuotesAsync(102), Times.Never);
    }
}
