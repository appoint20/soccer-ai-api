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
        public const string Draw = "draw";
    }

    /// <summary>
    /// Display labels for the bet each market evaluates. The 1X2 market has two
    /// of them because only the stronger side is ever evaluated.
    /// </summary>
    public static class Selections
    {
        public const string Btts = "BTTS";
        public const string Over25 = "Over 2.5 Goals";
        public const string Goals23 = "2-3 Goals";
        public const string MatchWinnerHome = "Match Winner (Home)";
        public const string MatchWinnerAway = "Match Winner (Away)";
        public const string Under25 = "Under 2.5 Goals";
        public const string Draw = "Draw";
    }

    /// <summary>
    /// Evaluate all markets for one fixture through the value gate:
    /// valid odds → odds ≥ MinOdds → EV ≥ MinEdge → p ≥ floor → confluence.
    /// No valid odds = "analysis only", never a pick.
    /// </summary>
    public static DecisionAudit Evaluate(
        WeightedPrediction prediction,
        StrategicSignals s,
        MarketPrices prices,
        double tierExtraProbability,
        ConfluenceOptions opt,
        StrategyOptions strat,
        AiAnalysisDto? ai = null)
    {
        // Winner pick = the stronger non-draw side; the draw is its own market.
        var favoriteIsHome = prediction.HomeProb >= prediction.AwayProb;
        var winnerProb = Math.Max(prediction.HomeProb, prediction.AwayProb);
        var winnerOdds = favoriteIsHome ? prices.HomeWin : prices.AwayWin;

        var markets = new List<MarketRuleAudit>
        {
            EvaluateBtts(prediction.BTTSProb, s, opt.BttsMinProbability + tierExtraProbability,
                prices.BttsYes, strat.MinOddsBtts, opt.BttsMinEdge, opt),
            EvaluateOver25(prediction.Over25Prob, s, opt.Over25MinProbability + tierExtraProbability,
                prices.Over25, strat.MinOddsOver25, opt.Over25MinEdge, opt),
            EvaluateGoals23(prediction.TwoToThreeGoalsProb, s, opt.Goals23MinProbability + tierExtraProbability,
                prices.Goals23, strat.MinOddsGoals23, opt.Goals23MinEdge, opt),
            EvaluateWinner(winnerProb, favoriteIsHome, s, opt.WinnerMinProbability + tierExtraProbability,
                winnerOdds, strat.MinOdds1X2, opt.WinnerMinEdge, opt),
            EvaluateUnder25(1 - prediction.Over25Prob, s, opt.Under25MinProbability + tierExtraProbability,
                prices.Under25, strat.MinOddsUnder25, opt.Under25MinEdge, opt),
            EvaluateDraw(prediction.DrawProb, s, opt.DrawMinProbability + tierExtraProbability,
                prices.Draw, strat.MinOdds1X2, opt.DrawMinEdge, opt)
        };

        // The language model's view is folded in last, as one visible rule per
        // market, so agreement and disagreement are both auditable rather than
        // being a hidden adjustment to a number.
        markets = [.. markets.Select(m => WithAiOpinion(m, prediction, ai, opt))];

        return new DecisionAudit(opt.MinConfirmations, markets, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Does the language model back this market?
    /// </summary>
    /// <remarks>
    /// Returns null when the AI produced no decision layer for the fixture —
    /// which is different from "it said no". Treating an absent opinion as a
    /// rejection would let a failed AI sync quietly disqualify every market on
    /// the board.
    /// </remarks>
    public static bool? AiBacks(string market, WeightedPrediction prediction, AiAnalysisDto? ai)
    {
        if (ai is null || !ai.HasDecisionLayer) return null;

        return market switch
        {
            Markets.Btts => ai.AiBttsQualified,
            Markets.Over25 => ai.AiOver25Qualified,
            Markets.Under25 => ai.AiUnder25Qualified,
            Markets.Goals23 => ai.AiGoals23Qualified,
            // The winner market is evaluated for one side only, so the AI is
            // asked about that same side rather than about "a winner".
            Markets.MatchWinner => prediction.HomeProb >= prediction.AwayProb
                ? ai.AiHomeWinQualified
                : ai.AiAwayWinQualified,
            // The AI is never asked about the draw, and silence is not a no.
            _ => null,
        };
    }

    /// <summary>
    /// Re-assembles one market with the AI agreement rule applied, under the
    /// configured mode.
    /// </summary>
    private static MarketRuleAudit WithAiOpinion(
        MarketRuleAudit m,
        WeightedPrediction prediction,
        AiAnalysisDto? ai,
        ConfluenceOptions opt)
    {
        m = m with { ModelOnlyQualified = m.Qualified, ModelOnlyComboEligible = m.ComboEligible,
            AiAgreementMode = opt.AiAgreement.ToString().ToLowerInvariant() };
        var backs = AiBacks(m.Market, prediction, ai);
        if (backs is null || opt.AiAgreement == ConfluenceOptions.AiAgreementMode.Ignore)
            return m with { AiAgrees = backs };

        var agrees = backs.Value;
        var evidence = agrees
            ? $"The model and the AI both back {m.Selection}"
            : $"The AI does not back {m.Selection}; the model does";

        var rules = m.Rules.ToList();
        rules.Add(agrees
            ? new RuleResult($"{m.Market}_confirm_ai_agrees", RuleResult.Confirm, true, evidence)
            : new RuleResult(
                $"{m.Market}_veto_ai_disagrees",
                opt.AiAgreement == ConfluenceOptions.AiAgreementMode.Veto ? RuleResult.Veto : RuleResult.Confirm,
                opt.AiAgreement == ConfluenceOptions.AiAgreementMode.Veto,
                evidence));

        var confirms = rules.Count(r => r is { Kind: RuleResult.Confirm, Fired: true });
        var vetoes = rules.Count(r => r is { Kind: RuleResult.Veto, Fired: true });

        // Re-run only the two gates the new rule can move. Everything upstream
        // of it — price, edge, probability floor — is unchanged by an opinion.
        var stillQualified = m.Qualified;
        var outcome = m.GateOutcome;

        if (!agrees && opt.AiAgreement == ConfluenceOptions.AiAgreementMode.Veto && m.Qualified)
        {
            stillQualified = false;
            outcome = GateOutcome.AiDisagrees;
        }
        else if (agrees && !m.Qualified && outcome == GateOutcome.InsufficientConfirms
                 && confirms >= opt.MinConfirmations)
        {
            // Agreement was the confirmation this market was short of.
            stillQualified = true;
            outcome = GateOutcome.Qualified;
        }

        return m with
        {
            Rules = rules,
            ConfirmationsFired = confirms,
            VetoesFired = vetoes,
            Qualified = stillQualified,
            GateOutcome = outcome,
            AiAgrees = backs,
            ComboEligible = !opt.InformationalOnlyMarkets.Contains(m.Market) && m.Odds is not null && m.Ev > 0 &&
                vetoes == 0 && confirms >= opt.MinConfirmations,
            KellyStake = stillQualified && m.Odds is { } odds && m.KellyFraction is { } fraction
                ? ValueMath.FractionalKelly(m.Probability, odds, fraction) : null,
        };
    }

    // ── BTTS ─────────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateBtts(
        double probability, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
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

        return Assemble(Markets.Btts, Selections.Btts,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── Over 2.5 ─────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateOver25(
        double probability, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
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

        return Assemble(Markets.Over25, Selections.Over25,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── 2-3 Goals ────────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateGoals23(
        double probability, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
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

        return Assemble(Markets.Goals23, Selections.Goals23,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── Match winner ─────────────────────────────────────────────────────────

    public static MarketRuleAudit EvaluateWinner(
        double probability, bool favoriteIsHome, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
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

        return Assemble(Markets.MatchWinner,
            favoriteIsHome ? Selections.MatchWinnerHome : Selections.MatchWinnerAway,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── Under 2.5 (low scoring) ──────────────────────────────────────────────

    public static MarketRuleAudit EvaluateUnder25(
        double probability, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
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

        return Assemble(Markets.Under25, Selections.Under25,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── Draw (1X2 draw outcome — recommendable since v3) ────────────────────

    public static MarketRuleAudit EvaluateDraw(
        double probability, StrategicSignals s, double threshold,
        double? odds, double minOdds, double minEdge, ConfluenceOptions opt)
    {
        var tableDataPresent = s.Table.HomeRank.Value > 0 && s.Table.AwayRank.Value > 0;

        var rules = new List<RuleResult>
        {
            Confirm("draw_confirm_tight_profiles",
                s.HomeForm.TightGameShareLast10.Flag && s.AwayForm.TightGameShareLast10.Flag,
                $"{s.HomeForm.TightGameShareLast10.Label}; {s.AwayForm.TightGameShareLast10.Label}"),

            Confirm("draw_confirm_close_ppg",
                tableDataPresent && s.Table.PpgGap.Value < opt.DrawPpgGapMax,
                s.Table.PpgGap.Label),

            Confirm("draw_confirm_h2h_draws",
                s.H2H.SampleSize >= opt.MinH2HSample &&
                s.H2H.DrawRateLast5.Value >= opt.DrawH2HRateConfirm,
                s.H2H.DrawRateLast5.Label),

            // Low combined scoring profile (proxy for low λ — lambdas are not
            // persisted in the math cache; venue goal averages stand in).
            Confirm("draw_confirm_low_scoring",
                s.HomeScoring.AvgTotalGoalsLast5.Value > 0 &&
                s.HomeScoring.AvgTotalGoalsLast5.Value < opt.DrawLowScoringAvgGoals &&
                s.AwayScoring.AvgTotalGoalsLast5.Value > 0 &&
                s.AwayScoring.AvgTotalGoalsLast5.Value < opt.DrawLowScoringAvgGoals,
                $"{s.HomeScoring.AvgTotalGoalsLast5.Label}; {s.AwayScoring.AvgTotalGoalsLast5.Label}"),

            Veto("draw_veto_chaos",
                s.HomeScoring.AvgTotalGoalsLast5.Flag || s.AwayScoring.AvgTotalGoalsLast5.Flag,
                $"{s.HomeScoring.AvgTotalGoalsLast5.Label}; {s.AwayScoring.AvgTotalGoalsLast5.Label}"),

            Veto("draw_veto_h2h_dominance",
                s.H2H.Dominance.Flag,
                s.H2H.Dominance.Label)
        };

        return Assemble(Markets.Draw, Selections.Draw,
            probability, threshold, odds, minOdds, minEdge, rules, opt);
    }

    // ── Assembly ─────────────────────────────────────────────────────────────

    private static RuleResult Confirm(string id, bool fired, string evidence) =>
        new(id, RuleResult.Confirm, fired, evidence);

    private static RuleResult Veto(string id, bool fired, string evidence) =>
        new(id, RuleResult.Veto, fired, evidence);

    /// <summary>
    /// Strips the bare "n/a" placeholders out of a rule's evidence.
    /// </summary>
    /// <remarks>
    /// Evidence is written as "{home label}; {away label}", and an unmeasured
    /// signal's label is literally "n/a" — so a fixture missing both sides
    /// published "n/a; n/a" to the reader as though it were a finding. Dropping
    /// the placeholders keeps whichever side WAS measured ("n/a; 2 clean sheets
    /// in last 5 away matches" becomes the away half alone) and empties the
    /// evidence only when nothing at all was measured.
    ///
    /// Done here rather than at each of the ~38 interpolation sites: one choke
    /// point every rule already passes through cannot be forgotten by the next
    /// rule someone adds.
    ///
    /// Descriptive absences ("No head-to-head history", "Standings not
    /// available") are left alone — those explain themselves and are worth
    /// reading.
    /// </remarks>
    public static RuleResult NormaliseEvidence(RuleResult rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Evidence)) return rule;
        if (!rule.Evidence.Contains(SignalValue.NotAvailable, StringComparison.OrdinalIgnoreCase))
            return rule;

        var kept = rule.Evidence
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.Equals(SignalValue.NotAvailable, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Empty means "nothing was measurable here" — the client says so in
        // words rather than printing a placeholder that reads like evidence.
        return rule with { Evidence = string.Join("; ", kept) };
    }

    /// <summary>
    /// The value gate, in order:
    /// 1. valid odds exist (else analysis only)
    /// 2. odds ≥ MinOdds floor
    /// 3. EV = p×odds − 1 ≥ MinEdge
    /// 4. p ≥ probability floor
    /// 5. zero vetoes
    /// 6. ≥ K confirms
    /// </summary>
    private static MarketRuleAudit Assemble(
        string market, string selection, double probability, double threshold,
        double? odds, double minOdds, double minEdge,
        List<RuleResult> rules, ConfluenceOptions opt)
    {
        rules = rules.Select(NormaliseEvidence).ToList();

        var probabilityPassed = probability >= threshold;
        var confirms = rules.Count(r => r is { Kind: RuleResult.Confirm, Fired: true });
        var vetoes = rules.Count(r => r is { Kind: RuleResult.Veto, Fired: true });

        var ev = odds is not null ? (double?)Math.Round(ValueMath.Ev(probability, odds.Value), 4) : null;

        // v5: MinOdds is enforced at TICKET level (TicketBuilder), not per leg.
        var outcome =
            opt.InformationalOnlyMarkets.Contains(market) ? GateOutcome.InformationalOnly
            : odds is null ? GateOutcome.AnalysisOnlyNoOdds
            : ev < minEdge ? GateOutcome.BelowMinEdge
            : !probabilityPassed ? GateOutcome.BelowProbabilityFloor
            : vetoes > 0 ? GateOutcome.Vetoed
            : confirms < opt.MinConfirmations ? GateOutcome.InsufficientConfirms
            : GateOutcome.Qualified;

        var qualified = outcome == GateOutcome.Qualified;

        // Combo-leg eligibility: any positive edge + full confluence. Weaker than
        // 'qualified' (no MinEdge/floor) — sub-floor favorites become combo legs.
        var comboEligible =
            !opt.InformationalOnlyMarkets.Contains(market) &&
            odds is not null && ev > 0 &&
            vetoes == 0 && confirms >= opt.MinConfirmations;

        return new MarketRuleAudit(
            market,
            Math.Round(probability, 4),
            Math.Round(threshold, 4),
            probabilityPassed,
            confirms,
            vetoes,
            qualified,
            rules)
        {
            Odds = odds,
            MinOdds = minOdds,
            Ev = ev,
            MinEdge = minEdge,
            KellyFraction = opt.KellyFraction,
            KellyStake = qualified && odds is not null
                ? ValueMath.FractionalKelly(probability, odds.Value, opt.KellyFraction)
                : null,
            GateOutcome = outcome,
            ComboEligible = comboEligible,
            Selection = selection
        };
    }
}
