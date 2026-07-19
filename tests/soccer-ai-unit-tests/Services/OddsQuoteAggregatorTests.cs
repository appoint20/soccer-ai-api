using FluentAssertions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class OddsQuoteAggregatorTests
{
    [Fact]
    public void BestPrices_TakesHighestValidPricePerMarket()
    {
        var quotes = new List<OddsQuote>
        {
            new("Bet365", OddsMarkets.HomeWin, 2.10),
            new("Pinnacle", OddsMarkets.HomeWin, 2.20),   // best
            new("Bwin", OddsMarkets.HomeWin, 2.05),
            new("Bet365", OddsMarkets.Over25, 1.85),
            new("Pinnacle", OddsMarkets.Over25, 1.90)     // best
        };

        var best = OddsQuoteAggregator.BestPrices(quotes);

        best.HomeWin.Should().Be(2.20, "line shopping = free EV");
        best.Over25.Should().Be(1.90);
        best.Draw.Should().BeNull("no draw quotes present");
    }

    [Fact]
    public void BestPrices_IgnoresGuardInvalidPrices()
    {
        var quotes = new List<OddsQuote>
        {
            new("BadBook", OddsMarkets.BttsYes, 185),  // locale-corrupted
            new("BadBook2", OddsMarkets.BttsYes, 1.0), // no payout
            new("GoodBook", OddsMarkets.BttsYes, 1.80)
        };

        OddsQuoteAggregator.BestPrices(quotes).BttsYes.Should().Be(1.80);
    }

    [Fact]
    public void BestPrices_AllInvalid_Null()
    {
        var quotes = new List<OddsQuote> { new("BadBook", OddsMarkets.AwayWin, 320) };
        OddsQuoteAggregator.BestPrices(quotes).AwayWin.Should().BeNull("corrupted prices never surface");
    }

    [Fact]
    public void NewOrChanged_KeepsOpeningsAndMovementsOnly()
    {
        var stored = new List<(string, string, double)>
        {
            ("Bet365", OddsMarkets.HomeWin, 2.10),
            ("Bet365", OddsMarkets.Over25, 1.85)
        };
        var fetched = new List<OddsQuote>
        {
            new("Bet365", OddsMarkets.HomeWin, 2.10),   // unchanged → skip
            new("Bet365", OddsMarkets.Over25, 1.80),    // moved → keep
            new("Pinnacle", OddsMarkets.HomeWin, 2.25)  // new bookmaker → keep (opening)
        };

        var result = OddsQuoteAggregator.NewOrChanged(fetched, stored);

        result.Should().HaveCount(2);
        result.Should().Contain(q => q.Bookmaker == "Bet365" && q.Market == OddsMarkets.Over25 && q.Price == 1.80);
        result.Should().Contain(q => q.Bookmaker == "Pinnacle" && q.Price == 2.25);
    }

    [Fact]
    public void NewOrChanged_DropsInvalidPrices()
    {
        var fetched = new List<OddsQuote> { new("BadBook", OddsMarkets.HomeWin, 210) };
        OddsQuoteAggregator.NewOrChanged(fetched, []).Should().BeEmpty();
    }
}
