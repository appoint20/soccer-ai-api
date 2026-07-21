using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class OddsDriftCalculatorTests
{
    private static readonly DateTimeOffset Opening = new(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);

    private static FixtureOddsQuote Q(string market, double price, double hoursAfterOpening,
        string bookmaker = "Bet365") => new()
    {
        FixtureId = 1, Bookmaker = bookmaker, Market = market, Price = price,
        CapturedAtUtc = Opening.AddHours(hoursAfterOpening)
    };

    [Fact]
    public void FavoriteShortening_NegativeDrift()
    {
        var quotes = new List<FixtureOddsQuote>
        {
            Q(OddsMarkets.HomeWin, 2.00, 0),   // opening
            Q(OddsMarkets.AwayWin, 3.50, 0),
            Q(OddsMarkets.HomeWin, 1.80, 23),  // T-1h: favorite shortened
            Q(OddsMarkets.AwayWin, 3.90, 23)
        };

        var drift = OddsDriftCalculator.Compute(quotes);

        drift.Should().NotBeNull();
        drift!.FavoriteDriftPct.Should().BeApproximately(-0.10, 1e-9); // (1.80-2.00)/2.00
        drift.FavoriteDirection.Should().Contain("shortening");
    }

    [Fact]
    public void SingleSnapshot_NoDriftMeasurable()
    {
        var quotes = new List<FixtureOddsQuote>
        {
            Q(OddsMarkets.HomeWin, 2.00, 0),
            Q(OddsMarkets.AwayWin, 3.50, 0),
            Q(OddsMarkets.Over25, 1.85, 0)
        };

        var drift = OddsDriftCalculator.Compute(quotes);

        drift.Should().NotBeNull();
        drift!.FavoriteDriftPct.Should().BeNull("one capture window cannot show movement");
        drift.Over25DriftPct.Should().BeNull();
    }

    [Fact]
    public void Over25Drift_BestPriceAcrossBookmakersPerWindow()
    {
        var quotes = new List<FixtureOddsQuote>
        {
            Q(OddsMarkets.Over25, 1.80, 0),
            Q(OddsMarkets.Over25, 1.85, 0, "Pinnacle"),   // opening best = 1.85
            Q(OddsMarkets.Over25, 2.00, 20),
            Q(OddsMarkets.Over25, 2.05, 20, "Pinnacle")   // latest best = 2.05
        };

        var drift = OddsDriftCalculator.Compute(quotes);

        drift!.Over25DriftPct.Should().BeApproximately((2.05 - 1.85) / 1.85, 1e-4);
    }

    [Fact]
    public void CorruptedPrices_Ignored()
    {
        var quotes = new List<FixtureOddsQuote>
        {
            Q(OddsMarkets.HomeWin, 200, 0),   // locale-corrupt
            Q(OddsMarkets.HomeWin, 185, 23)
        };

        OddsDriftCalculator.Compute(quotes).Should().BeNull("no guard-valid prices at all");
    }

    [Fact]
    public void EmptyQuotes_Null() => OddsDriftCalculator.Compute([]).Should().BeNull();
}

public class InformationalOnlyGateTests
{
    [Fact]
    public void Goals23_IsInformationalOnly_EvenWithOddsAndConfluence()
    {
        var opt = new SoccerAi.Application.Options.ConfluenceOptions();
        var audit = SoccerAi.Application.Services.Decisions.ConfluenceRuleEngine.EvaluateGoals23(
            0.55, new SoccerAi.Application.Models.Signals.StrategicSignals(), 0.50,
            odds: 2.0, minOdds: 1.7, minEdge: 0.05, opt);

        audit.GateOutcome.Should().Be(SoccerAi.Application.Services.Decisions.GateOutcome.InformationalOnly);
        audit.Qualified.Should().BeFalse("goals_2_3 has no odds at source — permanently analysis-only");
    }

    [Fact]
    public void InformationalMarkets_AreConfigurable()
    {
        var opt = new SoccerAi.Application.Options.ConfluenceOptions { InformationalOnlyMarkets = [] };
        var audit = SoccerAi.Application.Services.Decisions.ConfluenceRuleEngine.EvaluateGoals23(
            0.55, new SoccerAi.Application.Models.Signals.StrategicSignals(), 0.50,
            odds: 2.0, minOdds: 1.7, minEdge: 0.05, opt);

        audit.GateOutcome.Should().NotBe(
            SoccerAi.Application.Services.Decisions.GateOutcome.InformationalOnly,
            "an empty list disables the informational gate");
    }
}
