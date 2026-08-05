using FluentAssertions;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// These rules decide what the published track record says, so the edge cases
/// matter more than the happy path: anything that silently records a loss, or a
/// win that was never paid, corrupts the only honest evidence the product has.
/// </summary>
public class MarketOutcomeTests
{
    private const string Ft = "FT";

    [Theory]
    [InlineData(2, 1, true)]   // 3 goals
    [InlineData(3, 0, true)]
    [InlineData(1, 1, false)]  // 2 goals
    [InlineData(0, 0, false)]
    public void Over25_SettlesOnTotalGoals(int home, int away, bool expected) =>
        MarketOutcome.Won("over25", "", home, away).Should().Be(expected);

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(3, 2, true)]
    [InlineData(2, 0, false)]
    [InlineData(0, 0, false)]
    public void Btts_RequiresBothSidesToScore(int home, int away, bool expected) =>
        MarketOutcome.Won("btts", "", home, away).Should().Be(expected);

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(0, 2, false)]
    public void MatchWinnerHome_UsesTheSelectionLabel(int home, int away, bool expected) =>
        MarketOutcome.Won("match_winner", "Match Winner (Home)", home, away).Should().Be(expected);

    [Fact]
    public void MatchWinner_WithAnUnknownSide_IsNotSettleable()
    {
        // Guessing a side would be a coin flip written into the record.
        MarketOutcome.Won("match_winner", "Match Winner", 2, 0).Should().BeNull();
    }

    [Fact]
    public void UnknownMarket_IsNotSettleable() =>
        MarketOutcome.Won("corners_over_9", "", 3, 1).Should().BeNull();

    // ── Status handling ──────────────────────────────────────────────────────

    [Fact]
    public void FinishedFixture_Settles() =>
        MarketOutcome.Settle("over25", "", Ft, 2, 1).Should().Be(SelectionOutcome.Won);

    [Fact]
    public void NotStartedFixture_StaysPending() =>
        MarketOutcome.Settle("over25", "", "NS", 0, 0).Should().Be(SelectionOutcome.Pending);

    [Theory]
    [InlineData("PST")]
    [InlineData("CANC")]
    [InlineData("ABD")]
    [InlineData("AWD")]
    public void AbandonedFixture_IsVoidNotLost(string status) =>
        MarketOutcome.Settle("btts", "", status, 0, 0).Should().Be(SelectionOutcome.Void);

    [Theory]
    [InlineData("AET")]
    [InlineData("PEN")]
    public void ExtraTime_IsVoid(string status)
    {
        // The stored goals include extra time, but goals markets are settled on
        // 90 minutes and the 90-minute score is not recoverable. Recording this
        // as a win would credit a payout the customer never received.
        MarketOutcome.Settle("over25", "", status, 3, 2).Should().Be(SelectionOutcome.Void);
    }

    [Fact]
    public void UnsettleableMarket_OnAFinishedFixture_IsVoidNotLost() =>
        MarketOutcome.Settle("corners_over_9", "", Ft, 3, 1).Should().Be(SelectionOutcome.Void);

    // ── Ticket combination ───────────────────────────────────────────────────

    [Fact]
    public void Ticket_WinsOnlyWhenEveryLegWins() =>
        MarketOutcome.Combine([SelectionOutcome.Won, SelectionOutcome.Won])
            .Should().Be(SelectionOutcome.Won);

    [Fact]
    public void Ticket_LosesWhenAnyLegLoses() =>
        MarketOutcome.Combine([SelectionOutcome.Won, SelectionOutcome.Lost])
            .Should().Be(SelectionOutcome.Lost);

    [Fact]
    public void Ticket_StaysPendingWhileAnyLegIsUndecided() =>
        MarketOutcome.Combine([SelectionOutcome.Won, SelectionOutcome.Pending])
            .Should().Be(SelectionOutcome.Pending);

    [Fact]
    public void Ticket_WithAVoidLeg_IsVoidNotRepriced()
    {
        // Re-pricing the surviving legs would record a result for a ticket that
        // was never offered at those odds.
        MarketOutcome.Combine([SelectionOutcome.Won, SelectionOutcome.Void])
            .Should().Be(SelectionOutcome.Void);
    }

    [Fact]
    public void Ticket_PendingBeatsVoid()
    {
        // A pending leg may still lose, so the ticket is not resolved yet.
        MarketOutcome.Combine([SelectionOutcome.Void, SelectionOutcome.Pending])
            .Should().Be(SelectionOutcome.Pending);
    }

    [Fact]
    public void Ticket_WithNoLegs_IsVoid() =>
        MarketOutcome.Combine([]).Should().Be(SelectionOutcome.Void);
}

public class TicketFingerprintTests
{
    private static readonly DateTimeOffset Board = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    private static TicketLeg Leg(int fixtureId, string market, double odds = 1.85) =>
        new(fixtureId, "Premier League", market, market.ToUpperInvariant(), 0.62, odds, 0.147);

    [Fact]
    public void SameTicket_HashesIdentically() =>
        TicketFingerprint.Compute(Board, "combo", [Leg(1, "btts"), Leg(2, "over25")])
            .Should().Be(TicketFingerprint.Compute(Board, "combo", [Leg(1, "btts"), Leg(2, "over25")]));

    [Fact]
    public void LegOrder_DoesNotChangeTheFingerprint() =>
        TicketFingerprint.Compute(Board, "combo", [Leg(1, "btts"), Leg(2, "over25")])
            .Should().Be(TicketFingerprint.Compute(Board, "combo", [Leg(2, "over25"), Leg(1, "btts")]));

    [Fact]
    public void PriceMovement_DoesNotCreateANewTicket()
    {
        // The line drifting between publication and kickoff must not produce a
        // second row for a bet the customer saw once.
        TicketFingerprint.Compute(Board, "single", [Leg(1, "btts", 1.85)])
            .Should().Be(TicketFingerprint.Compute(Board, "single", [Leg(1, "btts", 2.10)]));
    }

    [Fact]
    public void DifferentDay_IsADifferentTicket() =>
        TicketFingerprint.Compute(Board, "single", [Leg(1, "btts")])
            .Should().NotBe(TicketFingerprint.Compute(Board.AddDays(1), "single", [Leg(1, "btts")]));

    [Fact]
    public void DifferentLegs_AreDifferentTickets() =>
        TicketFingerprint.Compute(Board, "combo", [Leg(1, "btts"), Leg(2, "over25")])
            .Should().NotBe(TicketFingerprint.Compute(Board, "combo", [Leg(1, "btts"), Leg(3, "over25")]));

    [Fact]
    public void Fingerprint_FitsTheColumn() =>
        TicketFingerprint.Compute(Board, "single", [Leg(1, "btts")]).Should().HaveLength(64);
}
