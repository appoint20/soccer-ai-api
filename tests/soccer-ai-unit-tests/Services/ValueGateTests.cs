using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class ValueGateTests
{
    private static readonly ConfluenceOptions Opt = new();

    private static StrategicSignals ConfluentBtts() => new()
    {
        HomeScoring = new ScoringSignals
        {
            ScoredInLast3Venue = SignalValue.Of(3, true, "3/3"),
            ConcededInLast3Venue = SignalValue.Of(3, true, "3/3")
        },
        AwayScoring = new ScoringSignals
        {
            ScoredInLast3Venue = SignalValue.Of(3, true, "3/3"),
            ConcededInLast3Venue = SignalValue.Of(3, true, "3/3")
        },
        H2H = new HeadToHeadSignals
        {
            BttsRateLast5 = SignalValue.Of(0.8, true, "80%"),
            SampleSize = 5
        }
    };

    [Fact]
    public void NoValidOdds_IsAnalysisOnly_NeverAPick()
    {
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, ConfluentBtts(), 0.50, null, 1.7, 0.05, Opt);

        audit.GateOutcome.Should().Be(GateOutcome.AnalysisOnlyNoOdds);
        audit.Qualified.Should().BeFalse("no odds = analysis only, despite full confluence");
        audit.Ev.Should().BeNull();
        audit.KellyStake.Should().BeNull();
    }

    [Fact]
    public void SubFloorOdds_NoLongerRejectLegs_MinOddsIsTicketLevel()
    {
        // v5: odds 1.65 < the 1.70 floor, but EV = 0.65×1.65−1 = 0.0725 ≥ 0.05 —
        // the pick qualifies; the floor is enforced when building TICKETS.
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, ConfluentBtts(), 0.50, 1.65, 1.70, 0.05, Opt);

        audit.GateOutcome.Should().Be(GateOutcome.Qualified);
        audit.ComboEligible.Should().BeTrue();
    }

    [Fact]
    public void ComboEligible_NeedsOnlyPositiveEvAndConfluence()
    {
        // EV = 0.65×1.60−1 = 0.04: below MinEdge (no single pick) but positive → combo leg.
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, ConfluentBtts(), 0.50, 1.60, 1.70, 0.05, Opt);

        audit.GateOutcome.Should().Be(GateOutcome.BelowMinEdge);
        audit.Qualified.Should().BeFalse();
        audit.ComboEligible.Should().BeTrue("EV > 0 with full confluence makes a valid combo leg");
    }

    [Fact]
    public void ComboEligible_False_WithNegativeEvOrVeto()
    {
        var negativeEv = ConfluenceRuleEngine.EvaluateBtts(0.55, ConfluentBtts(), 0.50, 1.60, 1.70, 0.05, Opt);
        negativeEv.ComboEligible.Should().BeFalse("EV = 0.55×1.60−1 < 0");
    }

    [Fact]
    public void PositiveButThinEdge_RejectedByMinEdge()
    {
        // p=0.58, odds=1.75 → EV = 0.015 < 0.05
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.58, ConfluentBtts(), 0.50, 1.75, 1.70, 0.05, Opt);

        audit.Ev.Should().BeApproximately(0.015, 1e-9);
        audit.GateOutcome.Should().Be(GateOutcome.BelowMinEdge);
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void QualifiedPick_CarriesEvAndQuarterKellyStake()
    {
        // p=0.60, odds=2.0 → EV=0.20; full Kelly = 0.2/1 = 0.2 → quarter = 0.05
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.60, ConfluentBtts(), 0.50, 2.0, 1.70, 0.05, Opt);

        audit.GateOutcome.Should().Be(GateOutcome.Qualified);
        audit.Qualified.Should().BeTrue();
        audit.Ev.Should().BeApproximately(0.20, 1e-9);
        audit.KellyStake.Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void EvPassesButFloorFails_AttributedToFloor()
    {
        // p=0.45 at odds 2.6 → EV = 0.17 ≥ MinEdge, but floor 0.50 fails
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.45, ConfluentBtts(), 0.50, 2.6, 1.70, 0.05, Opt);

        audit.GateOutcome.Should().Be(GateOutcome.BelowProbabilityFloor);
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void ValueMath_EvAndKelly_KnownValues()
    {
        ValueMath.Ev(0.60, 2.0).Should().BeApproximately(0.20, 1e-12);
        ValueMath.Ev(0.50, 2.0).Should().BeApproximately(0.0, 1e-12);
        ValueMath.FractionalKelly(0.60, 2.0, 0.25).Should().BeApproximately(0.05, 1e-9);
        ValueMath.FractionalKelly(0.40, 2.0, 0.25).Should().Be(0, "negative edge → no stake");
    }

    // ── Draw market ──────────────────────────────────────────────────────────

    private static StrategicSignals DrawFriendly() => new()
    {
        HomeForm = new FormSignals { TightGameShareLast10 = SignalValue.Of(0.7, true, "70% one-goal games") },
        AwayForm = new FormSignals { TightGameShareLast10 = SignalValue.Of(0.8, true, "80% one-goal games") },
        Table = new TableContextSignals
        {
            HomeRank = SignalValue.Of(9, false, "rank 9"),
            AwayRank = SignalValue.Of(11, false, "rank 11"),
            PpgGap = SignalValue.Of(0.1, false, "PPG gap 0.10")
        },
        H2H = new HeadToHeadSignals
        {
            DrawRateLast5 = SignalValue.Of(0.6, true, "3 of 5 H2H drawn"),
            Dominance = SignalValue.Of(0, false, "no dominance"),
            SampleSize = 5
        },
        HomeScoring = new ScoringSignals { AvgTotalGoalsLast5 = SignalValue.Of(2.0, false, "2.0 avg") },
        AwayScoring = new ScoringSignals { AvgTotalGoalsLast5 = SignalValue.Of(1.8, false, "1.8 avg") }
    };

    [Fact]
    public void Draw_QualifiesWithOwnRules_AtFairPrice()
    {
        // p=0.33 at odds 3.6 → EV = 0.188; floor 0.30; four confirms, no vetoes
        var audit = ConfluenceRuleEngine.EvaluateDraw(0.33, DrawFriendly(), 0.30, 3.6, 2.10, 0.05, Opt);

        audit.ConfirmationsFired.Should().Be(4);
        audit.VetoesFired.Should().Be(0);
        audit.GateOutcome.Should().Be(GateOutcome.Qualified);
    }

    [Fact]
    public void Draw_H2HDominance_Vetoes()
    {
        var signals = DrawFriendly() with
        {
            H2H = new HeadToHeadSignals
            {
                DrawRateLast5 = SignalValue.Of(0.6, true, "60%"),
                Dominance = SignalValue.Of(5, true, "The home side is unbeaten in the last 5 H2H"),
                SampleSize = 5
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateDraw(0.33, signals, 0.30, 3.6, 2.10, 0.05, Opt);

        audit.Rules.Single(r => r.RuleId == "draw_veto_h2h_dominance").Fired.Should().BeTrue();
        audit.GateOutcome.Should().Be(GateOutcome.Vetoed);
    }

    [Fact]
    public void Draw_ChaoticScorers_Vetoed()
    {
        var signals = DrawFriendly() with
        {
            HomeScoring = new ScoringSignals { AvgTotalGoalsLast5 = SignalValue.Of(4.0, true, "chaos") }
        };

        var audit = ConfluenceRuleEngine.EvaluateDraw(0.33, signals, 0.30, 3.6, 2.10, 0.05, Opt);

        audit.Rules.Single(r => r.RuleId == "draw_veto_chaos").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void FromCalibrated_DrawCanBeTheRecommendedOutcome()
    {
        var prediction = WeightedPrediction.FromCalibrated(new SoccerAi.Application.Interfaces.CalibratedProbabilities
        {
            HomeWin = 0.32, Draw = 0.36, AwayWin = 0.32,
            Over25 = 0.5, Btts = 0.5, TwoToThreeGoals = 0.5
        });

        prediction.MatchWinner.Should().Be("draw");
        prediction.Confidence.Should().BeApproximately(0.36, 1e-9);
    }
}
