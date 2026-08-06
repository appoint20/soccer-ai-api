using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// PickSelector is the one place a decision becomes something we would sell,
/// so these tests pin the rules that decide what reaches a customer.
/// </summary>
public class PickSelectorTests
{
    private static readonly ConfluenceOptions Opt = new();
    private static readonly StrategyOptions Strat = new();

    private static readonly FixtureRef Fixture =
        new(1, "Premier League", "Arsenal", "Chelsea", new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));

    private static MarketRuleAudit Audit(
        string market,
        double probability = 0.62,
        double? odds = 1.85,
        bool qualified = true,
        bool comboEligible = true,
        string selection = "") =>
        new(market, probability, 0.50, true, 2, 0, qualified, [])
        {
            Odds = odds,
            Ev = odds is null ? null : Math.Round(probability * odds.Value - 1, 4),
            ComboEligible = comboEligible,
            Selection = selection
        };

    private static DecisionAudit AuditFor(params MarketRuleAudit[] markets) =>
        new(2, markets, DateTimeOffset.UtcNow);

    // ── Guard rails ──────────────────────────────────────────────────────────

    [Fact]
    public void NoAudit_ProducesNothing()
    {
        var selection = PickSelector.Select(Fixture, null, 0.5, Opt);

        selection.QualifiedLegs.Should().BeEmpty();
        selection.ComboEligibleLegs.Should().BeEmpty();
        selection.SameMatchPair.Should().BeNull();
        selection.ConfidencePick.Should().BeNull();
    }

    [Fact]
    public void MarketWithoutOdds_IsNeverALeg()
    {
        var audit = AuditFor(Audit("goals_2_3", odds: null));

        var selection = PickSelector.Select(Fixture, audit, null, Opt);

        selection.QualifiedLegs.Should().BeEmpty(
            "an unpriced market is analysis only — it can never be staked");
        selection.ComboEligibleLegs.Should().BeEmpty();
    }

    [Fact]
    public void CorruptedOdds_AreRejectedNotRescaled()
    {
        // 185 is the classic locale bug for 1.85. Guessing the decimal point
        // would put a fabricated price into EV maths.
        var audit = AuditFor(Audit("over25", odds: 185));

        PickSelector.Select(Fixture, audit, null, Opt)
            .QualifiedLegs.Should().BeEmpty();
    }

    // ── Legs ─────────────────────────────────────────────────────────────────

    [Fact]
    public void QualifiedMarket_BecomesALegCarryingTheAuditedNumbers()
    {
        var audit = AuditFor(Audit("btts", probability: 0.64, odds: 1.80, selection: "BTTS"));

        var leg = PickSelector.Select(Fixture, audit, null, Opt).QualifiedLegs.Should().ContainSingle().Subject;

        leg.FixtureId.Should().Be(Fixture.FixtureId);
        leg.League.Should().Be(Fixture.League);
        leg.Market.Should().Be("btts");
        leg.Selection.Should().Be("BTTS");
        leg.Probability.Should().Be(0.64);
        leg.Odds.Should().Be(1.80);
        leg.Ev.Should().BeApproximately(0.64 * 1.80 - 1, 1e-9);
    }

    [Fact]
    public void ComboEligibleButNotQualified_IsAComboLegOnly()
    {
        var audit = AuditFor(Audit("over25", qualified: false, comboEligible: true));

        var selection = PickSelector.Select(Fixture, audit, null, Opt);

        selection.QualifiedLegs.Should().BeEmpty();
        selection.ComboEligibleLegs.Should().ContainSingle();
    }

    [Fact]
    public void LegacyAuditWithoutSelectionLabel_FallsBackToTheMarketName()
    {
        // Snapshots written before the audit carried a selection label must not
        // guess a 1X2 side — the wrong side is worse than a vague one.
        var audit = AuditFor(Audit("match_winner", selection: ""));

        PickSelector.Select(Fixture, audit, null, Opt)
            .QualifiedLegs.Single().Selection.Should().Be("Match Winner");
    }

    // ── Same-match pair ──────────────────────────────────────────────────────

    [Fact]
    public void SameMatchPair_UsesTheTrueJointNotTheProduct()
    {
        var audit = AuditFor(
            Audit("btts", probability: 0.60, odds: 1.55),
            Audit("over25", probability: 0.58, odds: 1.35));

        var pair = PickSelector.Select(Fixture, audit, 0.52, Opt).SameMatchPair;

        pair.Should().NotBeNull();
        pair!.JointProbability.Should().Be(0.52);
        pair.JointProbability.Should().BeGreaterThan(0.60 * 0.58,
            "the two markets are correlated, so the joint exceeds the product");
        pair.BttsOdds.Should().Be(1.55);
        pair.Over25Odds.Should().Be(1.35);
    }

    [Fact]
    public void SameMatchPair_RequiresTheJointProbability()
    {
        var audit = AuditFor(Audit("btts", odds: 1.55), Audit("over25", odds: 1.35));

        PickSelector.Select(Fixture, audit, null, Opt).SameMatchPair
            .Should().BeNull("without the true joint there is no honest price to compute");
    }

    [Fact]
    public void SameMatchPair_RequiresBothLegsToStandOnTheirOwn()
    {
        var audit = AuditFor(
            Audit("btts", odds: 1.55),
            Audit("over25", odds: 1.35, comboEligible: false));

        PickSelector.Select(Fixture, audit, 0.52, Opt).SameMatchPair
            .Should().BeNull("pairing a bet we would not take alone multiplies its error");
    }

    // ── Confidence picks (Product 2) ─────────────────────────────────────────

    [Fact]
    public void ConfidencePick_TakesTheMostLikelyEligibleMarket()
    {
        var audit = AuditFor(
            Audit("btts", probability: 0.63),
            Audit("over25", probability: 0.71),
            Audit("match_winner", probability: 0.66));

        var pick = PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick;

        pick.Should().NotBeNull();
        pick!.Market.Should().Be("over25");
        pick.Probability.Should().Be(0.71);
    }

    [Fact]
    public void ConfidencePick_NeedsNoOdds()
    {
        var audit = AuditFor(Audit("btts", probability: 0.72, odds: null));

        PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick
            .Should().NotBeNull("Product 2 is a prediction, not a bet");
    }

    [Fact]
    public void ConfidencePick_RespectsTheProbabilityFloor()
    {
        var opt = new ConfluenceOptions { ConfidencePickMinProbability = 0.70 };
        var audit = AuditFor(Audit("btts", probability: 0.69));

        PickSelector.Select(Fixture, audit, null, opt).ConfidencePick.Should().BeNull();
    }

    [Fact]
    public void ConfidencePick_AppliesThePerMarketFloor()
    {
        // Over 2.5 defaults to a 0.65 floor: baseline v9 measured the
        // confidence-selected 60-65% band hitting 48.8% against a claimed 63.8%.
        var audit = AuditFor(Audit("over25", probability: 0.63));

        PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick.Should().BeNull();
    }

    [Fact]
    public void ConfidencePick_FiltersBeforeChoosingTheBest()
    {
        // Over 2.5 is the highest probability but sits under its own floor.
        // Selecting first and filtering second would drop the fixture entirely
        // and lose the perfectly publishable BTTS pick underneath it.
        var audit = AuditFor(
            Audit("over25", probability: 0.64),
            Audit("btts", probability: 0.62));

        var pick = PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick;

        pick.Should().NotBeNull();
        pick!.Market.Should().Be("btts");
    }

    [Fact]
    public void ConfidencePick_MarketsWithoutAnOverride_UseTheGlobalFloor()
    {
        PickSelector.ConfidenceFloorFor("btts", Opt).Should().Be(Opt.ConfidencePickMinProbability);
        PickSelector.ConfidenceFloorFor("over25", Opt).Should().Be(0.65);
    }

    [Fact]
    public void ConfidencePick_Over25AboveItsOwnFloor_IsStillPublished()
    {
        var audit = AuditFor(Audit("over25", probability: 0.72));

        PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick!
            .Market.Should().Be("over25");
    }

    [Fact]
    public void ConfidencePick_IgnoresMarketsOutsideTheProductTwoSet()
    {
        // The draw is deliberately excluded: a 60%+ draw never occurs, and
        // including 2-3 Goals would double-count the goals markets.
        var audit = AuditFor(Audit("draw", probability: 0.95), Audit("btts", probability: 0.64));

        PickSelector.Select(Fixture, audit, null, Opt).ConfidencePick!
            .Market.Should().Be("btts");
    }

    // ── Board assembly ───────────────────────────────────────────────────────

    [Fact]
    public void BuildTickets_DelegatesPricingRulesToTheTicketBuilder()
    {
        // Two independent fixtures, each with a priced goals market.
        var selections = new[]
        {
            PickSelector.Select(
                new FixtureRef(1, "Premier League", "A", "B", DateTimeOffset.UtcNow),
                AuditFor(Audit("over25", probability: 0.62, odds: 1.85)), null, Opt),
            PickSelector.Select(
                new FixtureRef(2, "Bundesliga", "C", "D", DateTimeOffset.UtcNow),
                AuditFor(Audit("btts", probability: 0.61, odds: 1.90)), null, Opt)
        };

        var tickets = PickSelector.BuildTickets(selections, Strat, Opt);

        tickets.Should().NotBeEmpty();
        tickets.Should().OnlyContain(t => t.TotalOdds >= TicketBuilder.TicketFloor(t.Legs, Strat),
            "every ticket must clear its own market floor");
        tickets.Should().OnlyContain(t => t.Ev > 0);
    }

    [Fact]
    public void BuildTickets_WithNothingSelected_ReturnsAnEmptyBoard()
    {
        PickSelector.BuildTickets([], Strat, Opt).Should().BeEmpty();
    }
}
