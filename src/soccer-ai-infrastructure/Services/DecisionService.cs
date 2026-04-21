using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Evaluates weighted predictions against team stats, H2H, and statistical models
/// to produce market qualification decisions. Delegates trap detection to ITrapDetectionService.
/// </summary>
public sealed class DecisionService(
    ITrapDetectionService trapDetection,
    IFeatureScoringEngine scoringEngine,
    ILeagueAdjustmentService leagueAdjuster,
    IExpectedValueEngine evEngine) : IDecisionService
{
    public Task<DecisionServiceResult> Evaluate(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction? prediction,
        StatisticalModels stats,
        AiAnalysisDto? aiContext = null)
    {
        var markets = new QualificationDecisions();

        if (prediction == null)
        {
            return Task.FromResult(new DecisionServiceResult
            {
                Markets = markets,
                Trap = new TrapDecision(),
                Qualification = new Qualification
                {
                    IsQualified = false,
                    CombinedProbability = 0,
                    Label = "No prediction available"
                }
            });
        }

        // ── Market decisions ──

        // Over 2.5
        string? over25Warning = null;
        var avgTotalScored = teamStats.Home.AvgGoalsScoredLast7 + teamStats.Away.AvgGoalsScoredLast7;
        
        double over25Score = scoringEngine.CalculateGoalScore(prediction.Over25Prob, teamStats, context.OddsOver25);
        
        if (avgTotalScored < 2.0)
            over25Warning = $"Low combined scoring ({avgTotalScored:F1} avg total goals)";
        
        markets.Over25 = new MarketDecision
        {
            IsQualified = over25Score > 50,
            Confidence = Math.Round(over25Score / 100.0, 3), // Store the score as a pseudo-probability for compatibility
            Reason = $"Score: {over25Score}/100" + (over25Warning != null ? $" | {over25Warning}" : "")
        };

        // BTTS
        string? bttsWarning = null;
        double bttsScore = scoringEngine.CalculateGoalScore(prediction.BTTSProb, teamStats, context.OddsBttsYes);
        
        if (teamStats.Home.CleanSheetRate > 0.60 || teamStats.Away.CleanSheetRate > 0.60)
            bttsWarning = "High clean sheet rate detected";
            
        markets.BTTS = new MarketDecision
        {
            IsQualified = bttsScore > 50,
            Confidence = Math.Round(bttsScore / 100.0, 3), // Store the score as a pseudo-probability for compatibility
            Reason = $"Score: {bttsScore}/100" + (bttsWarning != null ? $" | {bttsWarning}" : "")
        };

        // 2-3 Goals
        double goals23Score = scoringEngine.CalculateGoals23Score(prediction.TwoToThreeGoalsProb, teamStats, 1.90);
        markets.TwoToThreeGoals = new MarketDecision
        {
            IsQualified = goals23Score >= 60.0,
            Confidence = Math.Round(goals23Score / 100.0, 3),
            Reason = $"Score: {goals23Score}/100 (Precise range analysis)"
        };

        // Match Winner
        string? winnerWarning = null;
        if (prediction.Confidence < 0.45)
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

        // Draws are explicitly excluded from analysis as per user requirements
        markets.Draw = new DrawDecision { IsQualified = false, Score = 0, Label = "Excluded" };

        // ── EV check and Trap Integration ──
        
        var bundle = new ProbabilityBundle 
        { 
            Poisson = stats.Poisson, 
            MonteCarlo = stats.MonteCarlo,
            MarketCalibrated = null // Calibrated markets not strictly required for trap detection at this stage
        };
        var trapResult = trapDetection.Detect(bundle, prediction, context);
        var trap = new TrapDecision
        {
            IsTrap = trapResult.IsTrap,
            Reason = trapResult.Reason
        };

        double leagueModifier = leagueAdjuster.GetGoalThresholdModifier(context.LeagueName ?? "");

        if (markets.Over25.IsQualified)
        {
            double finalScore = over25Score + trapResult.PenaltyScore;
            double threshold = 65.0 + leagueModifier;
            if (finalScore < threshold) 
            {
                markets.Over25.IsQualified = false;
                markets.Over25.Reason += $" | Failed adjusted threshold {threshold}";
            }
            if (context.OddsOver25.GetValueOrDefault() > 0 && evEngine.CalculateEV(prediction.Over25Prob, context.OddsOver25) < 0)
            {
                markets.Over25.IsQualified = false;
                markets.Over25.Reason += " | Negative EV";
            }
        }
        
        if (markets.BTTS.IsQualified)
        {
            double finalScore = bttsScore + trapResult.PenaltyScore;
            double threshold = 65.0 + leagueModifier;
            if (finalScore < threshold) 
            {
                markets.BTTS.IsQualified = false;
                markets.BTTS.Reason += $" | Failed adjusted threshold {threshold}";
            }
            if (context.OddsBttsYes.GetValueOrDefault() > 0 && evEngine.CalculateEV(prediction.BTTSProb, context.OddsBttsYes) < 0)
            {
                markets.BTTS.IsQualified = false;
                markets.BTTS.Reason += " | Negative EV";
            }
        }

        // Overall qualification (Draws excluded)
        var bestProb = Math.Max(prediction.Over25Prob, Math.Max(prediction.BTTSProb, prediction.Confidence));
        
        // Qualification priority: Goals FIRST. MatchWinner only secondary.
        var isQualified = markets.Over25.IsQualified || 
                          markets.BTTS.IsQualified || 
                          markets.LowScoring.IsQualified || 
                          markets.MatchWinner.IsQualified;

        // ── Final decision tier ──
        var decision = PredictionDecision.NoBet;
        if (trap.IsTrap)
        {
            decision = PredictionDecision.Avoid;
        }
        else if (isQualified)
        {
            // First pass: tier by final feature score if the market qualified
            double maxScore = 0;
            if (markets.Over25.IsQualified) maxScore = Math.Max(maxScore, over25Score + trapResult.PenaltyScore);
            if (markets.BTTS.IsQualified) maxScore = Math.Max(maxScore, bttsScore + trapResult.PenaltyScore);
            
            // Map the score to a tier
            var scoreDecision = maxScore switch
            {
                >= 85 => PredictionDecision.StrongBet,
                >= 75 => PredictionDecision.SmallEdge, // Standard
                >= 65 => PredictionDecision.LeanBet,
                _ => PredictionDecision.NoBet
            };
            
            // Match Winner logic (hasn't been converted to Scoring Engine yet) relies on pure EV tiering
            var bestEv = 0.0;
            if (markets.MatchWinner.IsQualified)
            {
                double? odds = prediction.MatchWinner == "home" ? context.OddsHome : prediction.MatchWinner == "away" ? context.OddsAway : context.OddsDraw;
                if (odds.HasValue && odds.Value > 0) bestEv = evEngine.CalculateEV(prediction.Confidence, odds);
            }

            var evDecision = bestEv switch
            {
                >= 0.10 => PredictionDecision.StrongBet,
                >= 0.06 => PredictionDecision.SmallEdge,
                _ => PredictionDecision.NoBet
            };
            
            // Take the strongest conviction
            decision = (PredictionDecision)Math.Max((int)scoreDecision, (int)evDecision);
        }

        return Task.FromResult(new DecisionServiceResult
        {
            Markets = markets,
            Trap = trap,
            Qualification = new Qualification
            {
                IsQualified = isQualified,
                CombinedProbability = Math.Round(bestProb, 3),
                Label = isQualified ? "Qualified" : "Not qualified"
            },
            Decision = decision
        });
    }

    public async Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return await Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }

    // Draw scoring removed as Draws are excluded from analysis.
}
