using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class DateFixtureSyncTests
{
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static DateTimeOffset At(DateOnly day) => DateSyncPipelineTests.At(day);

    private sealed class Harness : IDisposable
    {
        public readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Mock<IApiFootballService> Api = new();
        public readonly FixtureSyncService Sync;
        public Harness()
        {
            var tiers = new Mock<ILeagueTierService>();
            tiers.Setup(x => x.GetSyncLeagueIds()).Returns([39]);
            var quota = Mock.Of<IApiQuotaTracker>(x => x.IsDailyQuotaCritical == true);
            Api.Setup(x => x.GetFixturesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([]);
            Api.Setup(x => x.GetFixtureOddsQuotesAsync(It.IsAny<int>())).ReturnsAsync([]);
            Sync = new(Api.Object, Db, tiers.Object, quota, Options.Create(new OddsSyncOptions()),
                Options.Create(new SyncOptions()), NullLogger<FixtureSyncService>.Instance);
        }
        public void Dispose() => Db.Dispose();
    }

    private static ApiFixture ApiMatch(int id, DateTimeOffset date, string status = "NS") =>
        new(id, date, status, null, null, null, null, 10, "Home", 20, "Away");

    [Fact]
    public async Task DateBeyondScheduledWindowIsImportedWithHistoryButNoOtherUpcomingDay()
    {
        using var h = new Harness();
        h.Api.Setup(x => x.GetFixturesAsync(39, It.IsAny<int>())).ReturnsAsync([
            ApiMatch(100, At(Day)), ApiMatch(101, At(Day.AddDays(1))),
            ApiMatch(102, DateTimeOffset.UtcNow.AddDays(-1), "FT")]);
        var result = await h.Sync.SyncLeagueForDateAsync(39, Day, default);
        result.Created.Should().Be(2);
        (await h.Db.Fixtures.Select(f => f.ApiId).ToListAsync()).Should().BeEquivalentTo([100, 102]);
        h.Api.Verify(x => x.GetFixtureOddsQuotesAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ReschedulingAwayFromRequestedDateRemovesTheOldKickoffFromThatDaysCandidates()
    {
        using var h = new Harness();
        h.Db.Fixtures.Add(new() { ApiId = 100, LeagueId = 39, Date = At(Day), Status = "NS", HomeTeamId = 10, AwayTeamId = 20 });
        await h.Db.SaveChangesAsync();
        h.Api.Setup(x => x.GetFixturesAsync(39, It.IsAny<int>())).ReturnsAsync([ApiMatch(100, At(Day.AddDays(1)))]);
        await h.Sync.SyncLeagueForDateAsync(39, Day, default);
        (await h.Db.Fixtures.SingleAsync()).Date.Should().Be(At(Day.AddDays(1)));
        var odds = await h.Sync.CaptureDateOddsAsync(Day, default);
        odds.Candidates.Should().Be(0);
    }

    [Fact]
    public async Task TbdAndCancelledStatusUpdatesCannotKeepOldUpcomingPricesEligible()
    {
        using var h = new Harness();
        h.Db.Fixtures.Add(new() { ApiId = 100, LeagueId = 39, Date = At(Day), Status = "NS", HomeTeamId = 10, AwayTeamId = 20 });
        await h.Db.SaveChangesAsync();
        h.Api.Setup(x => x.GetFixturesAsync(39, It.IsAny<int>())).ReturnsAsync([ApiMatch(100, At(Day), "TBD")]);
        await h.Sync.SyncLeagueForDateAsync(39, Day, default);
        (await h.Db.Fixtures.SingleAsync()).Status.Should().Be("TBD");
        (await h.Sync.CaptureDateOddsAsync(Day, default)).Candidates.Should().Be(0);
    }

    [Fact]
    public async Task ManualOddsRefreshUsesOnlyTargetUpcomingFixturesEvenIfRecentlyCheckedAndBeyondHorizon()
    {
        using var h = new Harness();
        var now = DateTimeOffset.UtcNow;
        h.Db.Fixtures.AddRange(
            new Fixture { ApiId = 100, LeagueId = 39, Date = At(Day), Status = "NS", BttsYesOdds = 2.1,
                OddsCheckedAtUtc = now, OddsUpdatedAtUtc = now, OddsBookmaker = "Bet365" },
            new Fixture { ApiId = 101, LeagueId = 39, Date = At(Day.AddDays(1)), Status = "NS" },
            new Fixture { ApiId = 102, LeagueId = 39, Date = At(Day), Status = "FT" },
            new Fixture { ApiId = 103, LeagueId = 999, Date = At(Day), Status = "NS" });
        await h.Db.SaveChangesAsync();
        h.Api.Setup(x => x.GetFixtureOddsQuotesAsync(100)).ReturnsAsync([new("Bet365", OddsMarkets.BttsYes, 1.6, now)]);
        var result = await h.Sync.CaptureDateOddsAsync(Day, default);
        result.Should().Be(new DateOddsReport(1, 1, 1, 0));
        (await h.Db.Fixtures.SingleAsync(f => f.ApiId == 100)).BttsYesOdds.Should().Be(1.6);
        (await h.Db.FixtureOddsQuotes.SingleAsync()).Price.Should().Be(1.6);
        h.Api.Verify(x => x.GetFixtureOddsQuotesAsync(100), Times.Once);
        h.Api.Verify(x => x.GetFixtureOddsQuotesAsync(It.Is<int>(i => i != 100)), Times.Never);
        h.Api.Verify(x => x.HasOddsCoverageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulEmptyOddsResponseWithdrawsOldPricesAndReportsNoFreshQuote()
    {
        using var h = new Harness();
        h.Db.Fixtures.Add(new() { ApiId = 100, LeagueId = 39, Date = At(Day), Status = "NS", BttsYesOdds = 2.1 });
        await h.Db.SaveChangesAsync();
        var result = await h.Sync.CaptureDateOddsAsync(Day, default);
        result.Checked.Should().Be(1); result.WithFreshBet365Prices.Should().Be(0);
        (await h.Db.Fixtures.SingleAsync()).BttsYesOdds.Should().BeNull();
    }

    [Theory]
    [InlineData("fixtures", HttpStatusCode.InternalServerError, "{}")]
    [InlineData("odds", HttpStatusCode.InternalServerError, "{}")]
    [InlineData("standings", HttpStatusCode.InternalServerError, "{}")]
    [InlineData("fixtures", HttpStatusCode.OK, "not-json")]
    [InlineData("odds", HttpStatusCode.OK, "not-json")]
    [InlineData("standings", HttpStatusCode.OK, "not-json")]
    [InlineData("fixtures", HttpStatusCode.OK, "{}")]
    [InlineData("odds", HttpStatusCode.OK, "{}")]
    [InlineData("standings", HttpStatusCode.OK, "{}")]
    public async Task FailedOrMalformedProviderCallsCannotLookLikeASuccessfulEmptySync(string kind, HttpStatusCode status, string body)
    {
        using var client = new HttpClient(new Reply(status, body)) { BaseAddress = new("https://football.test") };
        var api = new ApiFootballService(client, Mock.Of<IApiQuotaTracker>(), Mock.Of<IApiCallTracker>(), NullLogger<ApiFootballService>.Instance);
        Func<Task> call = kind switch
        {
            "fixtures" => async () => await api.GetFixturesAsync(39, 2026),
            "odds" => async () => await api.GetFixtureOddsQuotesAsync(100),
            _ => async () => await api.GetStandingsAsync(39, 2026, default)
        };
        await call.Should().ThrowAsync<ExternalApiException>();
    }

    [Theory]
    [InlineData("fixtures")] [InlineData("odds")] [InlineData("standings")]
    public async Task LegitimateEmptyProviderResponsesRemainValid(string kind)
    {
        using var client = new HttpClient(new Reply(HttpStatusCode.OK, "{\"response\":[],\"errors\":[]}")) { BaseAddress = new("https://football.test") };
        var api = new ApiFootballService(client, Mock.Of<IApiQuotaTracker>(), Mock.Of<IApiCallTracker>(), NullLogger<ApiFootballService>.Instance);
        if (kind == "fixtures") (await api.GetFixturesAsync(39, 2026)).Should().BeEmpty();
        else if (kind == "odds") (await api.GetFixtureOddsQuotesAsync(100)).Should().BeEmpty();
        else (await api.GetStandingsAsync(39, 2026, default)).Should().BeEmpty();
    }

    private sealed class Reply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
