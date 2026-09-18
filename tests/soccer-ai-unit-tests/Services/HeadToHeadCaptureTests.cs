using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Head-to-head was computed from our own fixtures table, which holds the
/// fourteen synced leagues. Two teams whose history is in a cup or a division
/// below therefore read as never having met, and the panel — which needs three
/// meetings — stayed hidden on 103 of 203 upcoming matches on production.
/// These tests cover fetching that history and reading it back.
/// </summary>
public class HeadToHeadCaptureTests : IDisposable
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

    private Fixture Upcoming(int id = 1) => new()
    {
        Id = id, ApiId = 1000 + id, LeagueId = 39, Status = "NS",
        Date = Now.AddDays(2), HomeTeamId = 10, AwayTeamId = 20
    };

    private static ApiFixture Meeting(int apiId, int daysAgo, int homeGoals, int awayGoals, string status = "FT") =>
        new(apiId, Now.AddDays(-daysAgo), status, homeGoals, awayGoals, null, null, 10, "Home", 20, "Away");

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task MeetingsFromAnyCompetitionAreStoredAndTheFixtureIsMarkedChecked()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetHeadToHeadAsync(10, 20, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Meeting(5001, 400, 2, 1), Meeting(5002, 700, 0, 0)]);

        var checkedCount = await Sync().CaptureHeadToHeadAsync(CancellationToken.None);

        checkedCount.Should().Be(1);
        _db.HeadToHeadMeetings.Should().HaveCount(2);
        (await _db.Fixtures.SingleAsync()).HeadToHeadCheckedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// Finished matches do not change, so a pairing is worth exactly one
    /// request. Asking again every sync would spend the daily budget on an
    /// answer we already have.
    /// </summary>
    [Fact]
    public async Task ACheckedFixtureIsNeverAskedAboutAgain()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetHeadToHeadAsync(10, 20, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Meeting(5001, 400, 2, 1)]);

        await Sync().CaptureHeadToHeadAsync(CancellationToken.None);
        var second = await Sync().CaptureHeadToHeadAsync(CancellationToken.None);

        second.Should().Be(0);
        _api.Verify(x => x.GetHeadToHeadAsync(10, 20, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A pairing that genuinely never met is asked about once, not forever.</summary>
    [Fact]
    public async Task AnEmptyAnswerStillCountsAsChecked()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetHeadToHeadAsync(10, 20, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Sync().CaptureHeadToHeadAsync(CancellationToken.None);

        (await _db.Fixtures.SingleAsync()).HeadToHeadCheckedAtUtc.Should().NotBeNull();
        _db.HeadToHeadMeetings.Should().BeEmpty();
    }

    /// <summary>A second fixture between the same teams must not duplicate rows.</summary>
    [Fact]
    public async Task AMeetingAlreadyStoredIsNotWrittenTwice()
    {
        _db.Fixtures.AddRange(Upcoming(), new Fixture
        {
            Id = 2, ApiId = 2002, LeagueId = 39, Status = "NS",
            Date = Now.AddDays(3), HomeTeamId = 20, AwayTeamId = 10
        });
        await _db.SaveChangesAsync();
        _api.Setup(x => x.GetHeadToHeadAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Meeting(5001, 400, 2, 1)]);

        await Sync().CaptureHeadToHeadAsync(CancellationToken.None);

        _db.HeadToHeadMeetings.Should().ContainSingle("the pairing is the same in either direction");
    }

    /// <summary>Prices decide what can be published; a past meeting can wait.</summary>
    [Fact]
    public async Task ACriticalDailyBudgetStopsTheStepBeforeItSpends()
    {
        _db.Fixtures.Add(Upcoming());
        await _db.SaveChangesAsync();

        var checkedCount = await Sync(quotaCritical: true).CaptureHeadToHeadAsync(CancellationToken.None);

        checkedCount.Should().Be(0);
        _api.Verify(x => x.GetHeadToHeadAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The read path: fetched meetings join the ones already in the fixtures
    /// table, which is what lifts a pairing over the panel's three-meeting floor.
    /// </summary>
    [Fact]
    public async Task TheAnalysisReadsStoredFixturesAndFetchedMeetingsTogether()
    {
        _db.Teams.AddRange(new Team { ApiId = 10, Name = "Home" }, new Team { ApiId = 20, Name = "Away" });
        _db.Fixtures.AddRange(Upcoming(), new Fixture
        {
            Id = 9, ApiId = 9009, LeagueId = 39, Status = "FT", Date = Now.AddDays(-200),
            HomeTeamId = 10, AwayTeamId = 20, HomeGoal = 1, AwayGoal = 1
        });
        _db.HeadToHeadMeetings.AddRange(
            new HeadToHeadMeeting { ApiFixtureId = 5001, HomeTeamId = 10, AwayTeamId = 20, Date = Now.AddDays(-400), HomeGoals = 2, AwayGoals = 1 },
            new HeadToHeadMeeting { ApiFixtureId = 5002, HomeTeamId = 20, AwayTeamId = 10, Date = Now.AddDays(-700), HomeGoals = 0, AwayGoals = 3 },
            // The same match as fixture 9009: the richer fixture row must win.
            new HeadToHeadMeeting { ApiFixtureId = 9009, HomeTeamId = 10, AwayTeamId = 20, Date = Now.AddDays(-200), HomeGoals = 1, AwayGoals = 1 });
        await _db.SaveChangesAsync();

        var provider = new MatchDataProvider(_db, new TeamStatsService());
        var data = await provider.LoadAsync(await _db.Fixtures.SingleAsync(f => f.Id == 1), CancellationToken.None);

        data.H2H!.MatchesAnalyzed.Should().Be(3, "one stored fixture plus two fetched meetings, deduplicated");
        data.H2H.HomeWinRate.Should().BeGreaterThan(0);
    }
}
