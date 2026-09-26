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
/// One request returns every match in play anywhere, so the live loop costs the
/// tick rate and nothing more. What it must not do is spend that request when
/// none of our own fixtures could possibly be running — which is most of a day.
/// </summary>
public class LiveScoreCaptureTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IApiFootballService> _api = new();

    private FixtureSyncService Sync()
    {
        var tiers = new Mock<ILeagueTierService>();
        tiers.Setup(x => x.GetSyncLeagueIds()).Returns([39]);
        return new(_api.Object, _db, tiers.Object, Mock.Of<IApiQuotaTracker>(),
            Options.Create(new OddsSyncOptions()), Options.Create(new SyncOptions()),
            NullLogger<FixtureSyncService>.Instance);
    }

    private Fixture Kicked(double hoursAgo = 0.5, string status = "NS", int apiId = 1001, int id = 1) => new()
    {
        Id = id, ApiId = apiId, LeagueId = 39, Status = status,
        Date = Now.AddHours(-hoursAgo), HomeTeamId = 10, AwayTeamId = 20
    };

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task TheScoreAndTheMinuteAreWrittenToTheFixture()
    {
        _db.Fixtures.Add(Kicked());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetLiveFixturesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LiveFixtureState(1001, "1H", 31, null, 1, 0)]);

        var updated = await Sync().CaptureLiveScoresAsync(CancellationToken.None);

        updated.Should().Be(1);
        var fixture = await _db.Fixtures.SingleAsync();
        fixture.Status.Should().Be("1H");
        fixture.ElapsedMinutes.Should().Be(31);
        fixture.HomeGoal.Should().Be(1);
        fixture.LiveCheckedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// The saving that makes a one-minute loop affordable: with nothing of ours
    /// running, the database answers and the provider is never asked.
    /// </summary>
    [Fact]
    public async Task NothingOfOursInPlayCostsNoRequest()
    {
        _db.Fixtures.Add(Kicked(hoursAgo: -3));   // kicks off in three hours
        await _db.SaveChangesAsync();

        var updated = await Sync().CaptureLiveScoresAsync(CancellationToken.None);

        updated.Should().Be(0);
        _api.Verify(x => x.GetLiveFixturesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A match that finished hours ago is not asked about again.</summary>
    [Fact]
    public async Task AFinishedFixtureIsNotACandidate()
    {
        _db.Fixtures.Add(Kicked(status: "FT"));
        await _db.SaveChangesAsync();

        await Sync().CaptureLiveScoresAsync(CancellationToken.None);

        _api.Verify(x => x.GetLiveFixturesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A fixture stuck at "NS" long after kickoff stops being looked at, so an
    /// abandoned match cannot keep the loop spending requests for ever.
    /// </summary>
    [Fact]
    public async Task TheLookbackWindowIsBounded()
    {
        _db.Fixtures.Add(Kicked(hoursAgo: SyncOptions.LiveWindowHours + 1));
        await _db.SaveChangesAsync();

        await Sync().CaptureLiveScoresAsync(CancellationToken.None);

        _api.Verify(x => x.GetLiveFixturesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The response covers every competition the provider follows. Matches we
    /// do not sync are discarded rather than written against a wrong fixture.
    /// </summary>
    [Fact]
    public async Task MatchesFromOtherCompetitionsAreIgnored()
    {
        _db.Fixtures.Add(Kicked(apiId: 1001));
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetLiveFixturesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LiveFixtureState(999999, "2H", 70, null, 3, 3)]);

        var updated = await Sync().CaptureLiveScoresAsync(CancellationToken.None);

        updated.Should().Be(0);
        (await _db.Fixtures.SingleAsync()).HomeGoal.Should().Be(0);
    }

    // ── Live statistics ──────────────────────────────────────────────────────

    private async Task PublishPickOn(int fixtureId)
    {
        var ticket = new PublishedTicket { Id = 1, PublishedAtUtc = Now, Kind = "single" };
        _db.PublishedTickets.Add(ticket);
        _db.PublishedTicketLegs.Add(new PublishedTicketLeg
        {
            Id = 1, PublishedTicketId = ticket.Id, FixtureId = fixtureId,
            Market = "btts", Selection = "BTTS", Probability = .7, Odds = 1.9
        });
        await _db.SaveChangesAsync();
    }

    private static FixtureDetail Detail(int apiId) => new(apiId,
        new FixtureStats { ExpectedGoals = 1.4, TotalShots = 9, ShotsOnGoal = 4, BallPossession = 58 },
        new FixtureStats { ExpectedGoals = 0.6, TotalShots = 4, ShotsOnGoal = 1, BallPossession = 42 },
        0, 0);

    [Fact]
    public async Task StatisticsAreRefreshedForAnInPlayFixtureWeBacked()
    {
        var fixture = Kicked(status: "1H");
        fixture.ElapsedMinutes = 31;
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync();
        await PublishPickOn(fixture.Id);
        _api.Setup(x => x.GetFixtureDetailsBatchAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, FixtureDetail> { [1001] = Detail(1001) });

        var updated = await Sync().CaptureLiveStatsAsync(CancellationToken.None);

        updated.Should().Be(1);
        var stored = await _db.Fixtures.SingleAsync();
        stored.HomeObservedXg.Should().Be(1.4);
        stored.HomeBallPossession.Should().Be(58);
        stored.HomeShotsOnTarget.Should().Be(4);
        stored.StatisticsUpdatedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// The whole point of the restriction: statistics cost a request per twenty
    /// fixtures, so a live match nobody put a pick on is not followed.
    /// </summary>
    [Fact]
    public async Task AnInPlayFixtureWithNoPublishedPickIsNotFollowed()
    {
        var fixture = Kicked(status: "1H");
        fixture.ElapsedMinutes = 31;
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync();

        var updated = await Sync().CaptureLiveStatsAsync(CancellationToken.None);

        updated.Should().Be(0);
        _api.Verify(x => x.GetFixtureDetailsBatchAsync(It.IsAny<IReadOnlyCollection<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The loop ticks every minute; statistics do not. A reading taken moments
    /// ago is not worth another request.
    /// </summary>
    [Fact]
    public async Task FreshStatisticsAreNotFetchedAgain()
    {
        var fixture = Kicked(status: "1H");
        fixture.ElapsedMinutes = 31;
        fixture.StatisticsUpdatedAtUtc = Now.AddSeconds(-30);
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync();
        await PublishPickOn(fixture.Id);

        var updated = await Sync().CaptureLiveStatsAsync(CancellationToken.None);

        updated.Should().Be(0);
        _api.Verify(x => x.GetFixtureDetailsBatchAsync(It.IsAny<IReadOnlyCollection<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A fixture that has not kicked off has nothing to report.</summary>
    [Fact]
    public async Task AFixtureNotYetInPlayIsNotFollowed()
    {
        var fixture = Kicked(status: "NS");   // no elapsed minute recorded
        _db.Fixtures.Add(fixture);
        await _db.SaveChangesAsync();
        await PublishPickOn(fixture.Id);

        var updated = await Sync().CaptureLiveStatsAsync(CancellationToken.None);

        updated.Should().Be(0);
    }
}
