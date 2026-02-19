using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Comprehensive trap detection: low-score traps, market disagreements,
/// ultra-defensive setups. Uses LowScoreDetector for Poisson math.
/// </summary>
public sealed class TrapDetectionService : ITrapDetectionService
{
    private const double MarketDisagreementThreshold = 0.15;

    public TrapResult Detect(
        ProbabilityBundle bundle,
        WeightedPrediction? prediction,
        MatchContext odds)
    {
        if (prediction == null) return TrapResult.Safe;

        var lambdaHome = bundle.Poisson.ExpectedHomeGoals;
        var lambdaAway = bundle.Poisson.ExpectedAwayGoals;

        // ── 1. Low score trap (P(0-0) > 18%) ──
        bool lowScoreTrap = bundle.Poisson.IsValid &&
                            LowScoreDetector.IsLowScoringTrap(lambdaHome, lambdaAway);

        // ── 2. Market disagreement (model vs odds differ > 15 pp) ──
        bool marketMismatch = false;
        if (odds.OddsOver25 > 0 && bundle.MarketCalibrated != null)
        {
            marketMismatch = Math.Abs(prediction.Over25Prob - bundle.MarketCalibrated.Over25)
                             > MarketDisagreementThreshold;
        }

        // ── 3. Ultra-defensive match (both λ < 1.0) ──
        bool defensiveMatch = bundle.Poisson.IsValid &&
                              lambdaHome < 1.0 && lambdaAway < 1.0;

        // Build reason string
        var reasons = new List<string>();
        if (lowScoreTrap)
        {
            var p00 = LowScoreDetector.Probability00(lambdaHome, lambdaAway);
            reasons.Add($"P(0-0) = {p00:P0}");
        }
        if (marketMismatch) reasons.Add("Model vs market disagreement");
        if (defensiveMatch) reasons.Add($"Ultra-defensive (λH={lambdaHome:F2}, λA={lambdaAway:F2})");

        return new TrapResult
        {
            LowScoreTrap = lowScoreTrap,
            MarketMismatch = marketMismatch,
            DefensiveMatch = defensiveMatch,
            Reason = string.Join("; ", reasons)
        };
    }
}
