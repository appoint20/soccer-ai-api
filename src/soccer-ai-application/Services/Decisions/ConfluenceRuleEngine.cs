using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>
/// Transparent confluence rule engine. Pure and stateless:
/// calibrated DC probability + StrategicSignals in → audited decision out.
///
/// Signals gate decisions (confirm/veto/downgrade). They NEVER add to or
/// subtract from probabilities. Qualified requires: probability ≥ threshold
/// AND ≥ K confirms AND zero vetoes.
/// </summary>
public static class ConfluenceRuleEngine
{
    public static class Markets
    {
        public const string Btts = "btts";
        public const string Over25 = "over25";
        public const string Goals23 = "goals_2_3";
        public const string MatchWinner = "match_winner";
        public const string Under25 = "under25";
    }

    /// <summary>Evaluate all markets for one fixture.</summary>
    public static DecisionAudit Evaluate(
        WeightedPrediction prediction,
        StrategicSignals s,
        double tierExtraProbability,
        ConfluenceOptions opt)
    {
        var markets = new List<MarketRuleAudit>
        {
            EvaluateBtts(prediction.BTTSProb, s, opt.BttsMinProbability + tierExtraProbability, opt),
            EvaluateOver25(prediction.Over25Prob, s, opt.Over25MinProbability + tierExtraProbability, opt),
            EvaluateGoals23(prediction.TwoToThreeGoalsProb, s, opt.Goals23MinProbability + tierExtraProbability, opt),
            EvaluateWinner(prediction.Confidence, prediction.MatchWinner == "home", s,
                opt.WinnerMinProbability + tierExtraProbability, opt),
            EvaluateUnder25(1 - prediction.Over25Prob, s, opt.Under25MinProbability + tierExtraProbability, opt)
        };

        return new DecisionAudit(opt.MinConfirmations, markets, DateTimeOffset.UtcNow);
    }

    // ── BTTS ─────────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateBtts(
        double probability, StrategicSignals s, double threshold, ConfluenceOptions opt)
    {
        var rules = new List<RuleResult>
        {
            Confirm("btts_confirm_both_score_venue",
                s.HomeScoring.ScoredInLast3Venue.Value >= opt.ScoredInVenueConfirmCount &&
                s.AwayScoring.ScoredInLast3Venue.Value >= opt.ScoredInVenueConfirmCount,
                $"{s.HomeScoring.ScoredInLast3Venue.Label}; {s.AwayScoring.ScoredInLast3Venue.Label}"),

            Confirm("btts_confirm_both_concede_venue",
                s.HomeScoring.ConcededInLast3Venue.Value >= opt.ConcededInVenueConfirmCount &&
                s.AwayScoring.ConcededInLast3Venue.Value >= opt.ConcededInVenueConfirmCount,
                $"{s.HomeScoring.ConcededInLast3Venue.Label}; {s.AwayScoring.ConcededInLast3Venue.Label}"),

            Confirm("btts_confirm_h2h_rate",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                s.H2H.BttsRateLast5.Value >= opt.H2HBttsRateConfirm,
                s.H2H.BttsRateLast5.Label),

            Veto("btts_veto_clean_sheets",
                s.HomeScoring.CleanSheetsLast5Venue.Flag || s.AwayScoring.CleanSheetsLast5Venue.Flag,
                $"{s.HomeScoring.CleanSheetsLast5Venue.Label}; {s.AwayScoring.CleanSheetsLast5Venue.Label}"),

            Veto("btts_veto_failed_to_score",
                s.HomeScoring.FailedToScoreLast5Venue.Flag || s.AwayScoring.FailedToScoreLast5Venue.Flag,
                $"{s.HomeScoring.FailedToScoreLast5Venue.Label}; {s.AwayScoring.FailedToScoreLast5Venue.Label}")
        };

        return Assemble(Markets.Btts, probability, threshold, rules, opt);
    }

    // ── Over 2.5 ─────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateOver25(
        double probability, StrategicSignals s, double threshold, ConfluenceOptions opt)
    {
        var bothFormDeltasNegative = s.HomeForm.FormDelta.Value < 0 && s.AwayForm.FormDelta.Value < 0;

        var rules = new List<RuleResult>
        {
            Confirm("over25_confirm_both_venue_rates",
                s.HomeScoring.Over25RateLast5Venue.Flag && s.AwayScoring.Over25RateLast5Venue.Flag,
                $"{s.HomeScoring.Over25RateLast5Venue.Label}; {s.AwayScoring.Over25RateLast5Venue.Label}"),

            Confirm("over25_confirm_h2h_goals",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                (s.H2H.Over25RateLast5.Value >= opt.H2HOver25RateConfirm ||
                 s.H2H.AvgTotalGoals.Value >= opt.H2HOverAvgGoalsConfirm),
                $"{s.H2H.Over25RateLast5.Label}; {s.H2H.AvgTotalGoals.Label}"),

            Confirm("over25_confirm_league_deviation",
                s.League.HomeOver25VsLeague.Value > 0 && s.League.AwayOver25VsLeague.Value > 0 &&
                (s.League.HomeOver25VsLeague.Flag || s.League.AwayOver25VsLeague.Flag),
                $"{s.League.HomeOver25VsLeague.Label}; {s.League.AwayOver25VsLeague.Label}"),

            // Leaky defenses but historically quiet H2H → fixture plays out differently
            Veto("over25_veto_quiet_h2h",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                s.H2H.AvgTotalGoals.Value < opt.H2HQuietAvgGoals &&
                s.HomeScoring.ConcededInLast5Venue.Value >= opt.LeakyDefenseConcededCount &&
                s.AwayScoring.ConcededInLast5Venue.Value >= opt.LeakyDefenseConcededCount,
                $"{s.H2H.AvgTotalGoals.Label} despite leaky defenses"),

            Veto("over25_veto_dead_rubber_flat",
                (s.Table.HomeDeadRubber.Flag || s.Table.AwayDeadRubber.Flag) && bothFormDeltasNegative,
                $"{s.Table.HomeDeadRubber.Label}; {s.Table.AwayDeadRubber.Label}; both sides trending down"),

            Veto("over25_veto_under_profiles",
                s.HomeScoring.Under25RateLast5Venue.Flag && s.AwayScoring.Under25RateLast5Venue.Flag,
                $"{s.HomeScoring.Under25RateLast5Venue.Label}; {s.AwayScoring.Under25RateLast5Venue.Label}")
        };

        return Assemble(Markets.Over25, probability, threshold, rules, opt);
    }

    // ── 2-3 Goals ────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateGoals23(
        double probability, StrategicSignals s, double threshold, ConfluenceOptions opt)
    {
        var rules = new List<RuleResult>
        {
            Confirm("goals23_confirm_tight_games",
                s.HomeForm.TightGameShareLast10.Flag && s.AwayForm.TightGameShareLast10.Flag,
                $"{s.HomeForm.TightGameShareLast10.Label}; {s.AwayForm.TightGameShareLast10.Label}"),

            Confirm("goals23_confirm_h2h_band",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                s.H2H.AvgTotalGoals.Value >= opt.Goals23H2HBandLow &&
                s.H2H.AvgTotalGoals.Value <= opt.Goals23H2HBandHigh,
                s.H2H.AvgTotalGoals.Label),

            Confirm("goals23_confirm_moderate_totals",
                s.HomeScoring.AvgTotalGoalsLast5.Value >= opt.Goals23H2HBandLow &&
                s.HomeScoring.AvgTotalGoalsLast5.Value <= opt.Goals23H2HBandHigh &&
                s.AwayScoring.AvgTotalGoalsLast5.Value >= opt.Goals23H2HBandLow &&
                s.AwayScoring.AvgTotalGoalsLast5.Value <= opt.Goals23H2HBandHigh,
                $"{s.HomeScoring.AvgTotalGoalsLast5.Label}; {s.AwayScoring.AvgTotalGoalsLast5.Label}"),

            Veto("goals23_veto_chaos",
                s.HomeScoring.AvgTotalGoalsLast5.Value >= opt.ChaosVetoAvgGoals ||
                s.AwayScoring.AvgTotalGoalsLast5.Value >= opt.ChaosVetoAvgGoals,
                $"{s.HomeScoring.AvgTotalGoalsLast5.Label}; {s.AwayScoring.AvgTotalGoalsLast5.Label}"),

            Veto("goals23_veto_h2h_extremes",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                (s.H2H.AvgTotalGoals.Value < opt.Goals23H2HBandLow - 0.5 ||
                 s.H2H.AvgTotalGoals.Value > opt.Goals23H2HBandHigh + 0.5),
                s.H2H.AvgTotalGoals.Label)
        };

        return Assemble(Markets.Goals23, probability, threshold, rules, opt);
    }

    // ── Match winner ─────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateWinner(
        double probability, bool favoriteIsHome, StrategicSignals s, double threshold, ConfluenceOptions opt)
    {
        var favForm = favoriteIsHome ? s.HomeForm : s.AwayForm;
        var favTier2 = favoriteIsHome ? s.Schedule.HomeTier2Within4Days : s.Schedule.AwayTier2Within4Days;
        var favRank = favoriteIsHome ? s.Table.HomeRank.Value : s.Table.AwayRank.Value;
        var dogRank = favoriteIsHome ? s.Table.AwayRank.Value : s.Table.HomeRank.Value;
        var favSideWord = favoriteIsHome ? "home side" : "away side";
        var dogSideWord = favoriteIsHome ? "away side" : "home side";

        var tableDataPresent = favRank > 0 && dogRank > 0;

        var rules = new List<RuleResult>
        {
            // Spec composite: table edge AND trending flat-or-up AND no Tier2 rotation risk
            Confirm("winner_confirm_composite",
                tableDataPresent &&
                s.Table.RankGap.Flag && s.Table.PpgGap.Flag && favRank < dogRank &&
                favForm.FormDelta.Value >= 0 &&
                !favTier2.Flag,
                $"{s.Table.RankGap.Label}; {s.Table.PpgGap.Label}; {favForm.FormDelta.Label}; {favTier2.Label}"),

            Confirm("winner_confirm_venue_ppg",
                favForm.PpgLast5Venue.Value >= opt.WinnerVenuePpgConfirm,
                favForm.PpgLast5Venue.Label),

            Confirm("winner_confirm_h2h_dominance",
                s.H2H.Dominance.Flag && s.H2H.Dominance.Label.Contains(favSideWord),
                s.H2H.Dominance.Label),

            Veto("winner_veto_trap",
                s.Market.Trap.Flag,
                s.Market.Trap.Label),

            Veto("winner_veto_opposition_dominance",
                s.H2H.Dominance.Flag && s.H2H.Dominance.Label.Contains(dogSideWord),
                s.H2H.Dominance.Label),

            Veto("winner_veto_form_collapse",
                favForm.FormDelta.Flag && favForm.FormDelta.Value < 0,
                favForm.FormDelta.Label),

            Veto("winner_veto_rotation_risk",
                favTier2.Flag,
                favTier2.Label)
        };

        return Assemble(Markets.MatchWinner, probability, threshold, rules, opt);
    }

    // ── Under 2.5 (low scoring) ──────────────────────────────────────────────

    public static MarketRuleAudit EvaluateUnder25(
        double probability, StrategicSignals s, double threshold, ConfluenceOptions opt)
    {
        var rules = new List<RuleResult>
        {
            Confirm("under25_confirm_both_venue_rates",
                s.HomeScoring.Under25RateLast5Venue.Flag && s.AwayScoring.Under25RateLast5Venue.Flag,
                $"{s.HomeScoring.Under25RateLast5Venue.Label}; {s.AwayScoring.Under25RateLast5Venue.Label}"),

            Confirm("under25_confirm_quiet_h2h",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                s.H2H.AvgTotalGoals.Value < opt.H2HQuietAvgGoals,
                s.H2H.AvgTotalGoals.Label),

            Confirm("under25_confirm_defensive_profile",
                (s.HomeScoring.CleanSheetsLast5Venue.Flag || s.AwayScoring.FailedToScoreLast5Venue.Flag) &&
                (s.AwayScoring.CleanSheetsLast5Venue.Flag || s.HomeScoring.FailedToScoreLast5Venue.Flag),
                $"{s.HomeScoring.CleanSheetsLast5Venue.Label}; {s.AwayScoring.CleanSheetsLast5Venue.Label}"),

            Veto("under25_veto_chaos",
                s.HomeScoring.AvgTotalGoalsLast5.Flag || s.AwayScoring.AvgTotalGoalsLast5.Flag,
                $"{s.HomeScoring.AvgTotalGoalsLast5.Label}; {s.AwayScoring.AvgTotalGoalsLast5.Label}"),

            Veto("under25_veto_attack_trends",
                s.HomeScoring.AttackTrend.Flag && s.HomeScoring.AttackTrend.Value > 0 &&
                s.AwayScoring.AttackTrend.Flag && s.AwayScoring.AttackTrend.Value > 0,
                $"{s.HomeScoring.AttackTrend.Label}; {s.AwayScoring.AttackTrend.Label}")
        };

        return Assemble(Markets.Under25, probability, threshold, rules, opt);
    }

    // ── Assembly ─────────────────────────────────────────────────────────────

    private static RuleResult Confirm(string id, bool fired, string evidence) =>
        new(id, RuleResult.Confirm, fired, evidence);

    private static RuleResult Veto(string id, bool fired, string evidence) =>
        new(id, RuleResult.Veto, fired, evidence);

    private static MarketRuleAudit Assemble(
        string market, double probability, double threshold,
        List<RuleResult> rules, ConfluenceOptions opt)
    {
        var probabilityPassed = probability >= threshold;
        var confirms = rules.Count(r => r is { Kind: RuleResult.Confirm, Fired: true });
        var vetoes = rules.Count(r => r is { Kind: RuleResult.Veto, Fired: true });
        var qualified = probabilityPassed && confirms >= opt.MinConfirmations && vetoes == 0;

        return new MarketRuleAudit(
            market,
            Math.Round(probability, 4),
            Math.Round(threshold, 4),
            probabilityPassed,
            confirms,
            vetoes,
            qualified,
            rules);
    }
}
