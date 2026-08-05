using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The ledger is the product's evidence. These tests pin the properties that
/// make it trustworthy: it does not duplicate, it does not rewrite published
/// prices, and it never turns missing data into a loss.
/// </summary>
public class PickLedgerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PickLedger _sut;

    private static readonly DateOnly BoardDate = new(2026, 8, 8);

    public PickLedgerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _sut = new PickLedger(_db, new Mock<ILogger<PickLedger>>().Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketLeg Leg(int fixtureId, string market, string selection, double odds = 1.85) =>
        new(fixtureId, "Premier League", market, selection, 0.62, odds, 0.147);

    private static Ticket Single(int fixtureId, string market, string selection, double odds = 1.85) =>
        new([Leg(fixtureId, market, selection, odds)], odds, 0.62, 0.147, 0.05);

    private static DailyPickBoard Board(params Ticket[] tickets) =>
        new(BoardDate, tickets, [], new Dictionary<int, FixtureRef>(), new PickCoverage(1, 1, 1));

    private async Task SeedFixtureAsync(int id, string status, int homeGoals, int awayGoals)
    {
        _db.Fixtures.Add(new Fixture
        {
            Id = id,
            ApiId = id,
            LeagueId = 39,
            HomeTeamId = 1,
            AwayTeamId = 2,
            Date = new DateTimeOffset(BoardDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Status = status,
            HomeGoal = homeGoals,
            AwayGoal = awayGoals
        });
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ── Recording ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Record_StoresTheTicketAndItsLegs()
    {
        await _sut.RecordAsync(Board(Single(1, "btts", "BTTS", 1.90)));

        var ticket = await _db.PublishedTickets.Include(t => t.Legs).SingleAsync();
        ticket.Kind.Should().Be("single");
        ticket.Status.Should().Be(TicketStatus.Pending);
        ticket.TotalOdds.Should().Be(1.90);
        ticket.Legs.Should().ContainSingle();
        ticket.Legs[0].Market.Should().Be("btts");
    }

    [Fact]
    public async Task Record_IsIdempotent()
    {
        var board = Board(Single(1, "btts", "BTTS"));

        (await _sut.RecordAsync(board)).Should().Be(1);
        (await _sut.RecordAsync(board)).Should().Be(0, "the same board must not duplicate");

        _db.PublishedTickets.Should().ContainSingle();
    }

    [Fact]
    public async Task Record_KeepsThePublishedPriceWhenTheLineMoves()
    {
        await _sut.RecordAsync(Board(Single(1, "btts", "BTTS", 1.90)));
        await _sut.RecordAsync(Board(Single(1, "btts", "BTTS", 2.30)));

        // Re-recording at a better price would measure the closing line rather
        // than what the customer was actually shown.
        var ticket = await _db.PublishedTickets.SingleAsync();
        ticket.TotalOdds.Should().Be(1.90);
    }

    [Fact]
    public async Task Record_WithAnEmptyBoard_DoesNothing()
    {
        (await _sut.RecordAsync(Board())).Should().Be(0);
        _db.PublishedTickets.Should().BeEmpty();
    }

    // ── Settlement ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Settle_MarksAWinningSingleAsWon()
    {
        await _sut.RecordAsync(Board(Single(1, "over25", "Over 2.5 Goals")));
        await SeedFixtureAsync(1, "FT", 2, 1);

        (await _sut.SettleAsync()).Should().Be(1);

        (await _db.PublishedTickets.SingleAsync()).Status.Should().Be(TicketStatus.Won);
    }

    [Fact]
    public async Task Settle_MarksALosingSingleAsLost()
    {
        await _sut.RecordAsync(Board(Single(1, "over25", "Over 2.5 Goals")));
        await SeedFixtureAsync(1, "FT", 1, 0);

        await _sut.SettleAsync();

        (await _db.PublishedTickets.SingleAsync()).Status.Should().Be(TicketStatus.Lost);
    }

    [Fact]
    public async Task Settle_LeavesAnUnplayedFixturePending()
    {
        await _sut.RecordAsync(Board(Single(1, "over25", "Over 2.5 Goals")));
        await SeedFixtureAsync(1, "NS", 0, 0);

        (await _sut.SettleAsync()).Should().Be(0);

        (await _db.PublishedTickets.SingleAsync()).Status.Should().Be(TicketStatus.Pending);
    }

    [Fact]
    public async Task Settle_TreatsAMissingFixtureAsVoidNotLost()
    {
        // Absent data is not evidence of a losing bet.
        await _sut.RecordAsync(Board(Single(999, "over25", "Over 2.5 Goals")));

        await _sut.SettleAsync();

        (await _db.PublishedTickets.SingleAsync()).Status.Should().Be(TicketStatus.Void);
    }

    [Fact]
    public async Task Settle_RequiresEveryLegOfACombo()
    {
        var combo = new Ticket(
            [Leg(1, "over25", "Over 2.5 Goals"), Leg(2, "btts", "BTTS")],
            3.42, 0.38, 0.30, 0.04);

        await _sut.RecordAsync(Board(combo));
        await SeedFixtureAsync(1, "FT", 2, 1);   // over25 ✓
        await SeedFixtureAsync(2, "FT", 2, 0);   // btts ✗

        await _sut.SettleAsync();

        (await _db.PublishedTickets.SingleAsync()).Status.Should().Be(TicketStatus.Lost);
    }

    [Fact]
    public async Task Settle_DoesNotResettleAFinishedTicket()
    {
        await _sut.RecordAsync(Board(Single(1, "over25", "Over 2.5 Goals")));
        await SeedFixtureAsync(1, "FT", 2, 1);
        await _sut.SettleAsync();

        var settledAt = (await _db.PublishedTickets.SingleAsync()).SettledAtUtc;

        (await _sut.SettleAsync()).Should().Be(0, "settled results are history, not a cache");
        (await _db.PublishedTickets.SingleAsync()).SettledAtUtc.Should().Be(settledAt);
    }

    // ── Performance ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Performance_ComputesFlatStakeRoiFromSettledTicketsOnly()
    {
        await _sut.RecordAsync(Board(
            Single(1, "over25", "Over 2.5 Goals", 2.00),   // wins
            Single(2, "over25", "Over 2.5 Goals", 2.00),   // loses
            Single(3, "over25", "Over 2.5 Goals", 5.00))); // void

        await SeedFixtureAsync(1, "FT", 2, 1);
        await SeedFixtureAsync(2, "FT", 0, 0);
        await SeedFixtureAsync(3, "CANC", 0, 0);
        await _sut.SettleAsync();

        var overall = (await _sut.GetPerformanceAsync(BoardDate, BoardDate)).Overall;

        overall.Settled.Should().Be(2, "the voided ticket is excluded");
        overall.Won.Should().Be(1);
        overall.Voided.Should().Be(1);
        overall.Staked.Should().Be(2);
        overall.Returned.Should().Be(2.00);
        overall.Roi.Should().Be(0, "one win at 2.00 exactly repays two flat stakes");
        overall.HitRate.Should().Be(0.5);
    }

    [Fact]
    public async Task Performance_WithNothingSettled_ClaimsNothing()
    {
        var overall = (await _sut.GetPerformanceAsync(BoardDate, BoardDate)).Overall;

        overall.Settled.Should().Be(0);
        overall.Roi.Should().Be(0);
        overall.HitRate.Should().Be(0);
    }

    [Fact]
    public async Task Performance_ExcludesBoardsOutsideTheRange()
    {
        await _sut.RecordAsync(Board(Single(1, "btts", "BTTS")));

        var performance = await _sut.GetPerformanceAsync(BoardDate.AddDays(1), BoardDate.AddDays(7));

        performance.Overall.Pending.Should().Be(0);
    }

    [Fact]
    public async Task Performance_BreaksDownByMarket()
    {
        await _sut.RecordAsync(Board(
            Single(1, "btts", "BTTS", 2.00),
            Single(2, "over25", "Over 2.5 Goals", 2.00)));

        await SeedFixtureAsync(1, "FT", 1, 1);
        await SeedFixtureAsync(2, "FT", 0, 0);
        await _sut.SettleAsync();

        var byMarket = (await _sut.GetPerformanceAsync(BoardDate, BoardDate)).ByMarket;

        byMarket.Single(s => s.Key == "btts").Won.Should().Be(1);
        byMarket.Single(s => s.Key == "over25").Won.Should().Be(0);
    }
}
