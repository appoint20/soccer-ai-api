using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Odds are captured, never re-derived. Once a price is lost it cannot be
/// fetched again, so the rules about not erasing one are worth pinning.
/// </summary>
public class FixtureOddsWriterTests
{
    private static FixtureOdds Odds(
        double? homeWin = null, double? draw = null, double? awayWin = null,
        double? over25 = null, double? under25 = null, double? btts = null) =>
        new(homeWin, draw, awayWin, over25, under25, btts, null);

    [Fact]
    public void WritesEveryPresentMarket()
    {
        var fixture = new Fixture();

        FixtureOddsWriter.ApplyBestPrices(fixture, Odds(2.10, 3.40, 3.60, 1.85, 1.95, 1.80))
            .Should().BeTrue();

        fixture.HomeWinOdds.Should().Be(2.10);
        fixture.Over25Odds.Should().Be(1.85);
        fixture.BttsYesOdds.Should().Be(1.80);
    }

    [Fact]
    public void DoesNotEraseACapturedPriceWhenAMarketIsAbsent()
    {
        // The bookmaker dropping a market from this response says nothing about
        // the price we captured earlier — and that price cannot be re-fetched.
        var fixture = new Fixture { BttsYesOdds = 1.80, Over25Odds = 1.85 };

        FixtureOddsWriter.ApplyBestPrices(fixture, Odds(homeWin: 2.10));

        fixture.BttsYesOdds.Should().Be(1.80);
        fixture.Over25Odds.Should().Be(1.85);
        fixture.HomeWinOdds.Should().Be(2.10);
    }

    [Fact]
    public void ReportsNothingWrittenWhenEveryMarketIsAbsent()
    {
        var fixture = new Fixture();

        FixtureOddsWriter.ApplyBestPrices(fixture, Odds()).Should().BeFalse();
        fixture.UpdatedAt.Should().BeNull("an empty response is not a change");
    }

    [Fact]
    public void HasAnyValidPrice_NeedsOnlyOneMarket() =>
        FixtureOddsWriter.HasAnyValidPrice(null, null, null, null, null, 1.80)
            .Should().BeTrue();

    [Fact]
    public void HasAnyValidPrice_IsFalseWithoutAnyPrice() =>
        FixtureOddsWriter.HasAnyValidPrice(null, null, null, null, null, null)
            .Should().BeFalse();

    [Fact]
    public void HasAnyValidPrice_TreatsCorruptedPricesAsMissing()
    {
        // 185 is the German-locale reading of "1.85". A fixture holding only
        // corrupted values is unpriced, and the backfill must pick it up.
        FixtureOddsWriter.HasAnyValidPrice(185, 340, 360, null, null, null)
            .Should().BeFalse();
    }
}
