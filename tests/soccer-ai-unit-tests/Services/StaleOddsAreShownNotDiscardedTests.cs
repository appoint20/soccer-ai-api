using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Bookmakers reprice a fixture that is days away roughly once a day, so its
/// last real price is normally many hours old. Discarding it left every weekend
/// match with no price in the app until matchday. The price is now shown with
/// its age, and the separate question — may it be bet on — is still answered by
/// <see cref="LiveOddsPolicy.IsFresh"/>, which these tests hold to.
/// </summary>
public class StaleOddsAreShownNotDiscardedTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 21, 0, 0, TimeSpan.Zero);

    /// <summary>The measured production case: a Saturday fixture priced 15h ago.</summary>
    [Fact]
    public void ADayOldBet365PriceIsKeptOnTheFixtureButIsNotLive()
    {
        var fixture = new Fixture { Date = Now.AddHours(48) };

        FixtureOddsWriter.ReplaceLivePrices(fixture,
            [new("Bet365", OddsMarkets.BttsYes, 1.95, Now.AddHours(-15))], Now);

        fixture.BttsYesOdds.Should().Be(1.95);
        fixture.OddsUpdatedAtUtc.Should().Be(Now.AddHours(-15), "the age has to be readable");
        LiveOddsPolicy.IsFresh(fixture, Now).Should().BeFalse("15 hours is well past the betting window");
    }

    /// <summary>Age is not a licence to mix bookmakers: Bet365 remains the only source.</summary>
    [Fact]
    public void AnotherBookmakersPriceIsStillNeverShown()
    {
        var fixture = new Fixture { Date = Now.AddHours(48) };

        FixtureOddsWriter.ReplaceLivePrices(fixture,
            [new("Pinnacle", OddsMarkets.BttsYes, 2.4, Now.AddMinutes(-5))], Now);

        fixture.BttsYesOdds.Should().BeNull();
        fixture.OddsUpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void TheNewestBet365QuotePerMarketWinsRegardlessOfAge()
    {
        var fixture = new Fixture { Date = Now.AddHours(48) };

        FixtureOddsWriter.ReplaceLivePrices(fixture, [
            new("Bet365", OddsMarkets.BttsYes, 2.10, Now.AddHours(-20)),
            new("Bet365", OddsMarkets.BttsYes, 1.95, Now.AddHours(-15))], Now);

        fixture.BttsYesOdds.Should().Be(1.95);
    }

    /// <summary>
    /// The response carries the price, says whether it is live, and leaves the
    /// call alone: the decision was made from probability and evidence, so an
    /// old price neither confirms nor withdraws it.
    /// </summary>
    [Fact]
    public void TheResponseShowsTheStalePriceAndLeavesTheCallStanding()
    {
        var snapshot = new MatchAnalysis
        {
            Id = 1, Date = Now.AddHours(48), HomeTeam = "Home", AwayTeam = "Away",
            Prediction = new PredictionResponse(),
            DecisionAudit = new(2, [new("btts", .7, .5, true, 3, 0, true, [])
            {
                Odds = 1.95, MinOdds = 1.7, MinEdge = .05, Ev = .36, ComboEligible = true,
                GateOutcome = GateOutcome.Qualified, Selection = "BTTS"
            }], Now)
        };
        var fixture = new Fixture
        {
            Id = 1, Date = snapshot.Date, OddsBookmaker = "Bet365", BttsYesOdds = 1.95,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now.AddHours(-15)
        };

        LiveOddsPolicy.RefreshResponse(snapshot, fixture, Now);

        snapshot.OddsBttsYes.Should().Be(1.95, "the reader should see the last real market price");
        snapshot.OddsAreLive.Should().BeFalse();
        snapshot.OddsUpdatedAtUtc.Should().Be(Now.AddHours(-15));
        var market = snapshot.DecisionAudit!.Markets.Single();
        market.Qualified.Should().BeTrue("the call was never made from the price");
        market.Odds.Should().Be(1.95, "and the price is reported beside it");
        market.GateOutcome.Should().Be(GateOutcome.Qualified);
    }

    [Fact]
    public void AFreshPriceIsStillMarkedLiveAndStillQualifies()
    {
        var snapshot = new MatchAnalysis
        {
            Id = 1, Date = Now.AddHours(8), HomeTeam = "Home", AwayTeam = "Away",
            Prediction = new PredictionResponse(),
            DecisionAudit = new(2, [new("btts", .7, .5, true, 3, 0, true, [])
            {
                Odds = 1.95, MinOdds = 1.7, MinEdge = .05, Ev = .36, ComboEligible = true,
                GateOutcome = GateOutcome.Qualified, Selection = "BTTS"
            }], Now)
        };
        var fixture = new Fixture
        {
            Id = 1, Date = snapshot.Date, OddsBookmaker = "Bet365", BttsYesOdds = 1.95,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now.AddMinutes(-20)
        };

        LiveOddsPolicy.RefreshResponse(snapshot, fixture, Now);

        snapshot.OddsAreLive.Should().BeTrue();
        snapshot.DecisionAudit!.Markets.Single().Qualified.Should().BeTrue();
    }
}
