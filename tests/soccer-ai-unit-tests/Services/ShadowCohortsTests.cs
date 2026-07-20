using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class ShadowCohortsTests
{
    private static MarketRuleAudit Audit(
        string outcome, double? odds = 1.60, double? ev = 0.10, double minEdge = 0.05,
        bool probPassed = true, int confirms = 2, int vetoes = 0) =>
        new("match_winner", 0.62, 0.50, probPassed, confirms, vetoes,
            outcome == GateOutcome.Qualified, [])
        {
            Odds = odds,
            MinOdds = 2.10,
            Ev = ev,
            MinEdge = minEdge,
            GateOutcome = outcome
        };

    [Fact]
    public void BelowMinOdds_WithEdgeAndConfluence_IsMinOddsShadow()
    {
        // Odds 1.60 < 2.10 floor, but EV 0.10 ≥ 0.05 and everything downstream passes:
        // this pick was rejected SOLELY by the MinOdds floor.
        var cohorts = ShadowCohorts.Classify(Audit(GateOutcome.BelowMinOdds), minConfirms: 2);

        cohorts.Should().ContainSingle().Which.Should().Be(ShadowCohorts.RejectedByMinOdds);
    }

    [Fact]
    public void BelowMinOdds_ButNegativeEv_NotAShadow()
    {
        var cohorts = ShadowCohorts.Classify(
            Audit(GateOutcome.BelowMinOdds, ev: -0.02), minConfirms: 2);

        cohorts.Should().BeEmpty("the pick would ALSO have failed the EV gate — not a pure MinOdds rejection");
    }

    [Fact]
    public void BelowMinOdds_ButVetoed_NotAShadow()
    {
        var cohorts = ShadowCohorts.Classify(
            Audit(GateOutcome.BelowMinOdds, vetoes: 1), minConfirms: 2);

        cohorts.Should().BeEmpty();
    }

    [Fact]
    public void BelowMinEdge_WithConfluence_IsMinEvShadow()
    {
        var cohorts = ShadowCohorts.Classify(
            Audit(GateOutcome.BelowMinEdge, odds: 2.2, ev: 0.02), minConfirms: 2);

        cohorts.Should().ContainSingle().Which.Should().Be(ShadowCohorts.RejectedByMinEv);
    }

    [Fact]
    public void BelowMinEdge_WithTooFewConfirms_NotAShadow()
    {
        var cohorts = ShadowCohorts.Classify(
            Audit(GateOutcome.BelowMinEdge, confirms: 1), minConfirms: 2);

        cohorts.Should().BeEmpty();
    }

    [Fact]
    public void QualifiedPick_NeverInShadow()
    {
        ShadowCohorts.Classify(Audit(GateOutcome.Qualified), minConfirms: 2).Should().BeEmpty();
        ShadowCohorts.Classify(Audit(GateOutcome.AnalysisOnlyNoOdds, odds: null, ev: null), minConfirms: 2)
            .Should().BeEmpty();
    }

    // ── Winner-band hypothesis ───────────────────────────────────────────────

    private static readonly ConfluenceOptions Opt = new();

    [Theory]
    [InlineData(0.62, 1.40, true)]   // lower edges inclusive
    [InlineData(0.70, 1.80, true)]
    [InlineData(0.62, 2.09, true)]
    [InlineData(0.62, 2.10, false)]  // upper edge exclusive (2.10 belongs to the real gate)
    [InlineData(0.61, 1.80, false)]  // probability below band
    [InlineData(0.62, 1.39, false)]  // odds below band
    public void WinnerBand_BoundariesRespected(double p, double odds, bool expected) =>
        ShadowCohorts.InWinnerBand(p, odds, Opt).Should().Be(expected);

    [Fact]
    public void WinnerBand_NoOdds_NeverMatches() =>
        ShadowCohorts.InWinnerBand(0.70, null, Opt).Should().BeFalse();
}
