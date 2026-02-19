using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Combines all model outputs into a single weighted prediction.
/// 4-source consensus: 35% Poisson / 40% Monte Carlo / 15% ML / 10% Market calibrated
/// </summary>
public sealed class ProbabilityConsensusEngine : IProbabilityConsensusEngine
{
    // ── Model weights (4-source) ──────────────────────────────────
    private const double WPoisson = 0.35;
    private const double WMonteCarlo = 0.40;
    private const double WMl = 0.15;
    private const double WMarket = 0.10;

    public WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats)
    {
        if (bundle.MlPrediction == null)
            return null;

        var ml = bundle.MlPrediction;
        var hasMarket = bundle.MarketCalibrated != null;

        // Effective weights: redistribute market weight if no odds available
        var effPoisson = hasMarket ? WPoisson : WPoisson + WMarket * 0.50;
        var effMc = hasMarket ? WMonteCarlo : WMonteCarlo + WMarket * 0.30;
        var effMl = hasMarket ? WMl : WMl + WMarket * 0.20;
        var effMarket = hasMarket ? WMarket : 0;

        // ── Over 2.5 ──
        var pOver = bundle.Poisson.Over25 * effPoisson +
                    bundle.MonteCarlo.Over25 * effMc +
                    GetYes(ml.Over25) * effMl +
                    (bundle.MarketCalibrated?.Over25 ?? 0) * effMarket;

        // ── BTTS ──
        var pBtts = bundle.Poisson.BTTS * effPoisson +
                    bundle.MonteCarlo.BTTS * effMc +
                    GetYes(ml.Btts) * effMl +
                    (bundle.MarketCalibrated?.Btts ?? 0) * effMarket;

        // ── 2-3 Goals ──
        var p23 = bundle.Poisson.TwoToThreeGoals * effPoisson +
                  bundle.MonteCarlo.TwoToThreeGoals * effMc +
                  GetYes(ml.Goals2To3) * effMl;

        // ── Match Winner (HDA) ──
        var mlHda = ml.Hda.Probabilities;
        if (mlHda.Length < 3) mlHda = [0.33, 0.33, 0.33];

        var pHome = bundle.Poisson.HomeWin * effPoisson +
                    bundle.MonteCarlo.HomeWin * effMc +
                    mlHda[0] * effMl;

        var pDraw = bundle.Poisson.Draw * effPoisson +
                    bundle.MonteCarlo.Draw * effMc +
                    mlHda[1] * effMl;

        var pAway = bundle.Poisson.AwayWin * effPoisson +
                    bundle.MonteCarlo.AwayWin * effMc +
                    mlHda[2] * effMl;

        // Normalize HDA
        var total = pHome + pDraw + pAway;
        if (total > 0) { pHome /= total; pDraw /= total; pAway /= total; }

        var winner = "home";
        var confidence = pHome;

        if (pDraw > pHome && pDraw > pAway) { winner = "draw"; confidence = pDraw; }
        else if (pAway > pHome && pAway > pDraw) { winner = "away"; confidence = pAway; }

        return new WeightedPrediction
        {
            Over25 = pOver > 0.55,
            Over25Prob = Math.Clamp(pOver, 0, 1),
            BTTS = pBtts > 0.57,
            BTTSProb = Math.Clamp(pBtts, 0, 1),
            TwoToThreeGoals = p23 > 0.5,
            TwoToThreeGoalsProb = Math.Clamp(p23, 0, 1),
            MatchWinner = winner,
            Confidence = Math.Round(confidence, 2)
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static double GetYes(MarketPrediction mp)
    {
        if (mp.Probabilities.Length > 1) return mp.Probabilities[1];
        if (mp.Confidence > 0) return mp.Prediction ? mp.Confidence : 1 - mp.Confidence;
        return 0.0;
    }
}
