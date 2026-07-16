using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class ConfluenceRuleEngineTests
{
    private static readonly ConfluenceOptions Opt = new();

    private static SignalValue On(double value = 1, string label = "on") => SignalValue.Of(value, true, label);
    private static SignalValue Off(double value = 0, string label = "off") => SignalValue.Of(value, false, label);

    /// <summary>Signals that make BTTS look attractive with no vetoes.</summary>
    private static StrategicSignals BttsFriendly() => new()
    {
        HomeScoring = new ScoringSignals
        {
            ScoredInLast3Venue = SignalValue.Of(3, true, "home scored 3/3"),
            ConcededInLast3Venue = SignalValue.Of(2, false, "home conceded 2/3"),
            CleanSheetsLast5Venue = Off(1),
            FailedToScoreLast5Venue = Off(0)
        },
        AwayScoring = new ScoringSignals
        {
            ScoredInLast3Venue = SignalValue.Of(2, false, "away scored 2/3"),
            ConcededInLast3Venue = SignalValue.Of(3, true, "away conceded 3/3"),
            CleanSheetsLast5Venue = Off(1),
            FailedToScoreLast5Venue = Off(1)
        },
        H2H = new HeadToHeadSignals
        {
            BttsRateLast5 = SignalValue.Of(0.8, true, "BTTS in 80% of H2H"),
            SampleSize = 5
        }
    };

    // ── Qualification gate ───────────────────────────────────────────────────

    [Fact]
    public void Btts_Qualifies_WithProbabilityAndConfluence_NoVetoes()
    {
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.60, BttsFriendly(), 0.55, Opt);

        audit.ProbabilityPassed.Should().BeTrue();
        audit.ConfirmationsFired.Should().Be(3);
        audit.VetoesFired.Should().Be(0);
        audit.Qualified.Should().BeTrue();
    }

    [Fact]
    public void Btts_ProbabilityBelowThreshold_NeverQualifies()
    {
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.50, BttsFriendly(), 0.55, Opt);

        audit.ProbabilityPassed.Should().BeFalse();
        audit.Qualified.Should().BeFalse("probability gate failed despite 3 confirms");
    }

    [Fact]
    public void Btts_SingleVeto_BlocksQualification()
    {
        var signals = BttsFriendly() with
        {
            HomeScoring = BttsFriendly().HomeScoring with
            {
                CleanSheetsLast5Venue = SignalValue.Of(3, true, "home has 3 clean sheets")
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, signals, 0.55, Opt);

        audit.VetoesFired.Should().BeGreaterThan(0);
        audit.Qualified.Should().BeFalse("one veto is enough to block");
        audit.Rules.Single(r => r.RuleId == "btts_veto_clean_sheets").Fired.Should().BeTrue();
    }

    [Fact]
    public void Btts_TooFewConfirmations_NotQualified()
    {
        // Only the H2H confirm fires (1 < K = 2)
        var signals = new StrategicSignals
        {
            H2H = new HeadToHeadSignals
            {
                BttsRateLast5 = SignalValue.Of(0.8, true, "80%"),
                SampleSize = 5
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, signals, 0.55, Opt);

        audit.ConfirmationsFired.Should().Be(1);
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void Btts_H2HConfirm_RequiresMinimumSample()
    {
        var signals = BttsFriendly() with
        {
            H2H = new HeadToHeadSignals
            {
                BttsRateLast5 = SignalValue.Of(1.0, true, "BTTS in 100% of H2H"),
                SampleSize = 2 // below MinH2HSample = 3
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateBtts(0.65, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "btts_confirm_h2h_rate").Fired
            .Should().BeFalse("2 H2H meetings are not evidence");
    }

    // ── Over 2.5 vetoes ──────────────────────────────────────────────────────

    [Fact]
    public void Over25_QuietH2HDespiteLeakyDefenses_Vetoed()
    {
        var signals = new StrategicSignals
        {
            HomeScoring = new ScoringSignals
            {
                Over25RateLast5Venue = On(0.8),
                ConcededInLast5Venue = SignalValue.Of(5, true, "conceded in 5/5")
            },
            AwayScoring = new ScoringSignals
            {
                Over25RateLast5Venue = On(0.8),
                ConcededInLast5Venue = SignalValue.Of(4, false, "conceded in 4/5")
            },
            H2H = new HeadToHeadSignals
            {
                AvgTotalGoals = SignalValue.Of(1.4, false, "avg 1.4 goals in H2H"),
                SampleSize = 5
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateOver25(0.65, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "over25_veto_quiet_h2h").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void Over25_DeadRubberWithBothTrendingDown_Vetoed()
    {
        var signals = new StrategicSignals
        {
            Table = new TableContextSignals { HomeDeadRubber = On(1, "home dead rubber") },
            HomeForm = new FormSignals { FormDelta = SignalValue.Of(-0.6, true, "down") },
            AwayForm = new FormSignals { FormDelta = SignalValue.Of(-0.3, false, "down") }
        };

        var audit = ConfluenceRuleEngine.EvaluateOver25(0.65, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "over25_veto_dead_rubber_flat").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void Over25_Confirms_FromVenueRatesAndH2H()
    {
        var signals = new StrategicSignals
        {
            HomeScoring = new ScoringSignals { Over25RateLast5Venue = On(0.8) },
            AwayScoring = new ScoringSignals { Over25RateLast5Venue = On(0.6) },
            H2H = new HeadToHeadSignals
            {
                Over25RateLast5 = SignalValue.Of(0.8, true, "80% over"),
                AvgTotalGoals = SignalValue.Of(3.4, true, "3.4 avg"),
                SampleSize = 5
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateOver25(0.62, signals, 0.55, Opt);

        audit.ConfirmationsFired.Should().Be(2);
        audit.Qualified.Should().BeTrue();
    }

    // ── Match winner ─────────────────────────────────────────────────────────

    private static StrategicSignals WinnerFriendly(bool favoriteIsHome = true) => new()
    {
        Table = new TableContextSignals
        {
            HomeRank = SignalValue.Of(favoriteIsHome ? 2 : 15, false, "rank"),
            AwayRank = SignalValue.Of(favoriteIsHome ? 15 : 2, false, "rank"),
            RankGap = SignalValue.Of(13, true, "rank gap 13"),
            PpgGap = SignalValue.Of(0.9, true, "ppg gap 0.9")
        },
        HomeForm = new FormSignals
        {
            FormDelta = SignalValue.Of(favoriteIsHome ? 0.2 : -0.7, favoriteIsHome ? false : true, "delta"),
            PpgLast5Venue = SignalValue.Of(favoriteIsHome ? 2.4 : 1.0, favoriteIsHome, "venue ppg")
        },
        AwayForm = new FormSignals
        {
            FormDelta = SignalValue.Of(favoriteIsHome ? -0.7 : 0.2, favoriteIsHome ? true : false, "delta"),
            PpgLast5Venue = SignalValue.Of(favoriteIsHome ? 1.0 : 2.4, !favoriteIsHome, "venue ppg")
        },
        Schedule = new ScheduleSignals
        {
            HomeTier2Within4Days = Off(0, "no European match"),
            AwayTier2Within4Days = Off(0, "no European match")
        },
        Market = new MarketSignals { Trap = Off(0, "aligned with table") }
    };

    [Fact]
    public void Winner_CompositeConfirm_TableEdgeTrendingNoRotation()
    {
        var audit = ConfluenceRuleEngine.EvaluateWinner(0.62, true, WinnerFriendly(), 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "winner_confirm_composite").Fired.Should().BeTrue();
        audit.Rules.Single(r => r.RuleId == "winner_confirm_venue_ppg").Fired.Should().BeTrue();
        audit.Qualified.Should().BeTrue();
    }

    [Fact]
    public void Winner_Tier2Rotation_BreaksCompositeAndVetoes()
    {
        var signals = WinnerFriendly() with
        {
            Schedule = new ScheduleSignals
            {
                HomeTier2Within4Days = On(1, "European match in 2 days"),
                AwayTier2Within4Days = Off()
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateWinner(0.62, true, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "winner_confirm_composite").Fired.Should().BeFalse();
        audit.Rules.Single(r => r.RuleId == "winner_veto_rotation_risk").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void Winner_TrapSignal_Vetoes()
    {
        var signals = WinnerFriendly() with
        {
            Market = new MarketSignals { Trap = On(10, "market against table logic") }
        };

        var audit = ConfluenceRuleEngine.EvaluateWinner(0.62, true, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "winner_veto_trap").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    [Fact]
    public void Winner_OppositionDominance_Vetoes()
    {
        var signals = WinnerFriendly() with
        {
            H2H = new HeadToHeadSignals
            {
                Dominance = SignalValue.Of(5, true, "The away side is unbeaten in the last 5 H2H"),
                SampleSize = 5
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateWinner(0.62, true, signals, 0.55, Opt);

        audit.Rules.Single(r => r.RuleId == "winner_veto_opposition_dominance").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }

    // ── Full evaluation & audit trail ────────────────────────────────────────

    [Fact]
    public void Evaluate_ProducesAuditForAllFiveMarkets()
    {
        var prediction = new WeightedPrediction
        {
            BTTSProb = 0.6, Over25Prob = 0.6, TwoToThreeGoalsProb = 0.5,
            HomeProb = 0.6, AwayProb = 0.2, DrawProb = 0.2,
            Confidence = 0.6, MatchWinner = "home"
        };

        var audit = ConfluenceRuleEngine.Evaluate(prediction, BttsFriendly(), 0, Opt);

        audit.Markets.Should().HaveCount(5);
        audit.Markets.Select(m => m.Market).Should().BeEquivalentTo(
            ["btts", "over25", "goals_2_3", "match_winner", "under25"]);
        audit.MinConfirmationsRequired.Should().Be(Opt.MinConfirmations);
        audit.Markets.Should().OnlyContain(m => m.Rules.Count > 0,
            "every market must expose its full rule evaluation");
    }

    [Fact]
    public void Evaluate_Tier2ExtraProbability_RaisesThresholds()
    {
        var prediction = new WeightedPrediction
        {
            BTTSProb = 0.57, Over25Prob = 0.5, TwoToThreeGoalsProb = 0.4,
            HomeProb = 0.5, AwayProb = 0.3, DrawProb = 0.2,
            Confidence = 0.5, MatchWinner = "home"
        };

        var tier1 = ConfluenceRuleEngine.Evaluate(prediction, BttsFriendly(), 0, Opt);
        var tier2 = ConfluenceRuleEngine.Evaluate(prediction, BttsFriendly(), 0.05, Opt);

        tier1.Markets.Single(m => m.Market == "btts").ProbabilityPassed
            .Should().BeTrue("0.57 ≥ 0.55");
        tier2.Markets.Single(m => m.Market == "btts").ProbabilityPassed
            .Should().BeFalse("0.57 < 0.60 with the Tier2 uplift");
    }

    [Fact]
    public void Audit_FiredConfirmRuleIds_ExposesOnlyFiredConfirms()
    {
        var audit = ConfluenceRuleEngine.EvaluateBtts(0.60, BttsFriendly(), 0.55, Opt);

        audit.FiredConfirmRuleIds.Should().BeEquivalentTo(
            ["btts_confirm_both_score_venue", "btts_confirm_both_concede_venue", "btts_confirm_h2h_rate"]);
    }

    // ── Under 2.5 ────────────────────────────────────────────────────────────

    [Fact]
    public void Under25_DefensiveProfile_QualifiesWithQuietH2H()
    {
        // Defensive profile must hold in BOTH directions:
        // (home clean sheets OR away fails to score) → away won't score, AND
        // (away clean sheets OR home fails to score) → home won't score.
        var signals = new StrategicSignals
        {
            HomeScoring = new ScoringSignals
            {
                Under25RateLast5Venue = On(0.8),
                CleanSheetsLast5Venue = On(3),
                FailedToScoreLast5Venue = On(2), // home also struggles to score
                AvgTotalGoalsLast5 = Off(1.6)
            },
            AwayScoring = new ScoringSignals
            {
                Under25RateLast5Venue = On(0.8),
                FailedToScoreLast5Venue = On(2),
                AvgTotalGoalsLast5 = Off(1.4)
            },
            H2H = new HeadToHeadSignals
            {
                AvgTotalGoals = SignalValue.Of(1.5, false, "quiet H2H"),
                SampleSize = 4
            }
        };

        var audit = ConfluenceRuleEngine.EvaluateUnder25(0.62, signals, 0.55, Opt);

        audit.ConfirmationsFired.Should().Be(3);
        audit.VetoesFired.Should().Be(0);
        audit.Qualified.Should().BeTrue();
    }

    [Fact]
    public void Goals23_ChaosTeams_Vetoed()
    {
        var signals = new StrategicSignals
        {
            HomeScoring = new ScoringSignals { AvgTotalGoalsLast5 = SignalValue.Of(4.2, true, "chaos") },
            AwayScoring = new ScoringSignals { AvgTotalGoalsLast5 = SignalValue.Of(2.5, false, "normal") }
        };

        var audit = ConfluenceRuleEngine.EvaluateGoals23(0.5, signals, 0.45, Opt);

        audit.Rules.Single(r => r.RuleId == "goals23_veto_chaos").Fired.Should().BeTrue();
        audit.Qualified.Should().BeFalse();
    }
}
