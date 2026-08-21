using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// With no odds feed the value gate rejects every market before it looks at
/// probability, so the combo board was empty however good the model was. These
/// cover the unpriced path: combos are composed from probability alone, and
/// nothing about them may look stakeable.
/// </summary>
public class UnpricedComboTests
{
    private static ConfluenceOptions Options() => new()
    {
        AllowUnpricedCombos = true,
        UnpricedComboMinProbability = 0.20,
        MaxComboLegs = 3,
        MinConfirmations = 2,
    };

    private static StrategyOptions Strategy() => new();

    private static MarketRuleAudit Audit(
        string market, double probability, double? odds, bool passed = true,
        int confirms = 2, int vetoes = 0) =>
        new(market, probability, 0.5, passed, confirms, vetoes, Qualified: odds is not null,
            Rules: [])
        {
            Odds = odds,
            GateOutcome = odds is null ? GateOutcome.AnalysisOnlyNoOdds : GateOutcome.Qualified,
        };

    private static FixtureRef Fixture(int id) =>
        new(id, "Premier League", "Home", "Away", DateTimeOffset.UtcNow);

    private static FixtureSelection SelectUnpriced(int fixtureId, params MarketRuleAudit[] markets)
    {
        var audit = new DecisionAudit(2, markets, DateTimeOffset.UtcNow);
        return PickSelector.Select(Fixture(fixtureId), audit, null, Options());
    }

    [Fact]
    public void UnpricedMarketsBecomeComboLegs_WhenTheyPassEveryNonPriceCheck()
    {
        var selection = SelectUnpriced(1, Audit("over25", 0.64, odds: null));

        selection.UnpricedComboLegs.Should().ContainSingle();
        selection.QualifiedLegs.Should().BeEmpty("an unpriced market is never stakeable on its own");
    }

    [Fact]
    public void UnpricedMarketIsRejected_WhenItWouldNotHaveQualifiedAnyway()
    {
        // The bar is not lowered — only the price requirement is dropped.
        SelectUnpriced(1, Audit("over25", 0.64, null, passed: false))
            .UnpricedComboLegs.Should().BeEmpty("it failed the probability floor");

        SelectUnpriced(2, Audit("over25", 0.64, null, vetoes: 1))
            .UnpricedComboLegs.Should().BeEmpty("a veto fired");

        SelectUnpriced(3, Audit("over25", 0.64, null, confirms: 1))
            .UnpricedComboLegs.Should().BeEmpty("it lacked the required confirmations");
    }

    [Fact]
    public void InformationalMarketNeverBecomesAnUnpricedLeg()
    {
        SelectUnpriced(1, Audit("goals_2_3", 0.80, odds: null))
            .UnpricedComboLegs.Should().BeEmpty("2-3 goals can never become a bet");
    }

    [Fact]
    public void CombosAreBuiltFromUnpricedLegs_AndCarryNoPrice()
    {
        var selections = new[]
        {
            SelectUnpriced(1, Audit("over25", 0.70, odds: null)),
            SelectUnpriced(2, Audit("btts", 0.68, odds: null)),
        };

        var tickets = PickSelector.BuildTickets(selections, Strategy(), Options());

        var combo = tickets.Should().ContainSingle().Subject;
        combo.Legs.Should().HaveCount(2);

        combo.IsPriced.Should().BeFalse();
        combo.TotalOdds.Should().BeNull("no quote exists to multiply");
        combo.Ev.Should().BeNull("EV needs a price");
        combo.KellyStake.Should().BeNull("Kelly needs a price");

        // Still real: these come from the model, not from a bookmaker.
        combo.CombinedProbability.Should().BeApproximately(0.70 * 0.68, 1e-6);
        combo.FairOdds.Should().BeApproximately(1 / (0.70 * 0.68), 0.01);
    }

    [Fact]
    public void ThinUnpricedCombosAreSuppressed()
    {
        var selections = new[]
        {
            SelectUnpriced(1, Audit("over25", 0.35, odds: null)),
            SelectUnpriced(2, Audit("btts", 0.35, odds: null)),
        };

        // 0.35 × 0.35 = 0.1225, below the 0.20 floor.
        PickSelector.BuildTickets(selections, Strategy(), Options())
            .Should().BeEmpty();
    }

    [Fact]
    public void UnpricedCombosAreNotBuilt_WhenTheFeatureIsOff()
    {
        var off = Options();
        off.AllowUnpricedCombos = false;

        var audit = new DecisionAudit(2, [Audit("over25", 0.70, null)], DateTimeOffset.UtcNow);
        var selections = new[]
        {
            PickSelector.Select(Fixture(1), audit, null, off),
            PickSelector.Select(Fixture(2), audit with { }, null, off),
        };

        PickSelector.BuildTickets(selections, Strategy(), off).Should().BeEmpty();
    }

    [Fact]
    public void PricedCombosStillCarryTheirNumbers()
    {
        var selections = new[]
        {
            SelectUnpriced(1, Audit("over25", 0.70, odds: 2.0)),
            SelectUnpriced(2, Audit("btts", 0.68, odds: 2.0)),
        };

        var combo = PickSelector.BuildTickets(selections, Strategy(), Options())
            .Should().ContainSingle(t => t.Legs.Count == 2).Subject;

        combo.IsPriced.Should().BeTrue();
        combo.TotalOdds.Should().Be(4.0);
        combo.Ev.Should().NotBeNull();
        combo.KellyStake.Should().NotBeNull();
    }
}
