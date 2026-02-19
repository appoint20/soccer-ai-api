using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Evaluates weighted predictions against team stats, H2H, and statistical models
/// to produce market qualification decisions. Delegates trap detection to ITrapDetectionService.
/// </summary>
public sealed class DecisionService(
    ITrapDetectionService trapDetection,
    IExpectedValueEngine evEngine) : IDecisionService
{
    public DecisionServiceResult Evaluate(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction? prediction,
        StatisticalModels stats)
    {
        var markets = new QualificationDecisions();

        if (prediction == null)
        {
            return new DecisionServiceResult
            {
                Markets = markets,
                Trap = new TrapDecision(),
                Qualification = new Qualification
                {
                    IsQualified = false,
                    CombinedProbability = 0,
                    Label = "No prediction available"
                }
            };
        }

        // ── Market decisions ──

        // Over 2.5
        string? over25Warning = null;
        var avgTotalScored = teamStats.Home.AvgGoalsScoredLast7 + teamStats.Away.AvgGoalsScoredLast7;
        if (avgTotalScored < 2.0)
            over25Warning = $"Low combined scoring ({avgTotalScored:F1} avg total goals)";
        markets.Over25 = MarketDecision.Create(prediction.Over25Prob, over25Warning);

        // BTTS
        string? bttsWarning = null;
        if (teamStats.Home.CleanSheetRate > 0.60 || teamStats.Away.CleanSheetRate > 0.60)
            bttsWarning = "High clean sheet rate detected";
        markets.BTTS = MarketDecision.Create(prediction.BTTSProb, bttsWarning);

        // 2-3 Goals
        markets.TwoToThreeGoals = MarketDecision.Create(prediction.TwoToThreeGoalsProb);

        // Match Winner
        string? winnerWarning = null;
        if (prediction.Confidence < 0.40)
            winnerWarning = $"Very low confidence ({prediction.Confidence:P0})";
        markets.MatchWinner = MarketDecision.Create(prediction.Confidence, winnerWarning);

        // Low Scoring — use Poisson P(0-0) + Under 1.5 for proper detection
        var lambdaH = stats.Poisson.ExpectedHomeGoals;
        var lambdaA = stats.Poisson.ExpectedAwayGoals;
        var p00 = stats.Poisson.IsValid ? LowScoreDetector.Probability00(lambdaH, lambdaA) : 0;
        var pUnder15 = stats.Poisson.IsValid ? LowScoreDetector.ProbabilityUnder15(lambdaH, lambdaA) : 0;
        var lowScoringProb = Math.Max(pUnder15, 1.0 - prediction.Over25Prob);

        string? lowWarning = null;
        if (avgTotalScored > 2.5)
            lowWarning = $"High combined scoring ({avgTotalScored:F1} avg total goals)";

        if (lowScoringProb >= 0.55 && p00 > 0.10 && string.IsNullOrWhiteSpace(lowWarning))
        {
            markets.LowScoring = new MarketDecision
            {
                IsQualified = true,
                Confidence = Math.Round(lowScoringProb, 3),
                Reason = $"Low scoring profile: P(0-0)={p00:P0}, P(U1.5)={pUnder15:P0}"
            };
        }
        else
        {
            markets.LowScoring = new MarketDecision
            {
                IsQualified = false,
                Confidence = Math.Round(lowScoringProb, 3),
                Reason = lowWarning ?? $"Under 2.5 probability ({lowScoringProb:P0}) below threshold"
            };
        }

        // Draw
        var drawScore = CalculateDrawScore(stats, h2h, teamStats);
        markets.Draw = DrawDecision.Create(drawScore);

        // ── EV check (add warning if negative EV) ──
        if (markets.Over25.IsQualified && context.OddsOver25 > 0)
        {
            var ev = evEngine.CalculateEV(prediction.Over25Prob, context.OddsOver25);
            if (ev < 0) markets.Over25 = MarketDecision.Create(prediction.Over25Prob,
                $"Negative EV ({ev:+0.0%;-0.0%})");
        }
        if (markets.BTTS.IsQualified && context.OddsBttsYes > 0)
        {
            var ev = evEngine.CalculateEV(prediction.BTTSProb, context.OddsBttsYes);
            if (ev < 0) markets.BTTS = MarketDecision.Create(prediction.BTTSProb,
                $"Negative EV ({ev:+0.0%;-0.0%})");
        }

        // ── Trap (inline conversion to existing TrapDecision model) ──
        var trap = new TrapDecision
        {
            IsTrap = stats.Poisson.IsValid && LowScoreDetector.IsLowScoringTrap(lambdaH, lambdaA),
            Reason = stats.Poisson.IsValid && LowScoreDetector.IsLowScoringTrap(lambdaH, lambdaA)
                ? $"P(0-0)={p00:P0}" : string.Empty
        };

        // ── Overall qualification ──
        var bestProb = Math.Max(prediction.Over25Prob, Math.Max(prediction.BTTSProb, prediction.Confidence));
        var isQualified = markets.Over25.IsQualified || markets.BTTS.IsQualified ||
                          markets.MatchWinner.IsQualified || markets.LowScoring.IsQualified;

        return new DecisionServiceResult
        {
            Markets = markets,
            Trap = trap,
            Qualification = new Qualification
            {
                IsQualified = isQualified,
                CombinedProbability = Math.Round(bestProb, 3),
                Label = isQualified ? "Qualified" : "Not qualified"
            }
        };
    }

    public DecisionServiceResult Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }

    // ── Draw Score ───────────────────────────────────────────────

    private static double CalculateDrawScore(
        StatisticalModels stats,
        HeadToHeadModel h2h,
        TeamStatsResponse teamStats)
    {
        double score = 0;

        if (stats.Poisson.IsValid)
            score += stats.Poisson.Draw * 0.35;

        if (stats.MonteCarlo.IsValid)
            score += stats.MonteCarlo.Draw * 0.25;

        if (h2h.IsValid)
            score += h2h.DrawRate * 0.20;

        var avgDrawRate = (teamStats.Home.DrawRate + teamStats.Away.DrawRate) / 2.0;
        score += avgDrawRate * 0.20;

        return score;
    }
}
