using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Combines all model outputs into a single weighted prediction.
/// Uses per-market weights + league volatility adjustment + goal correlation
/// + H2H divergence boost + form momentum detection.
/// </summary>
public sealed class ProbabilityConsensusEngine(
    ILeagueVolatilityService volatility) : IProbabilityConsensusEngine
{
    // ── BTTS / Over 2.5 weights ──
    private const double BttsPoisson = 0.35;
    private const double BttsMc = 0.35;
    private const double BttsMl = 0.25;
    private const double BttsMarket = 0.05;

    // ── Winner / HDA weights ──
    private const double WinPoisson = 0.35;
    private const double WinMc = 0.35;
    private const double WinMl = 0.25;
    private const double WinMarket = 0.05;

    public WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats)
    {
        return Combine(bundle, stats, 0, null, null, null);
    }

    public WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats, int leagueId)
    {
        return Combine(bundle, stats, leagueId, null, null, null);
    }

    public WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats, int leagueId, HeadToHeadModel? h2h)
    {
        return Combine(bundle, stats, leagueId, h2h, null, null);
    }

    public WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats, int leagueId, HeadToHeadModel? h2h, string? geminiRecommendation, double? geminiConfidence)
    {
        if (bundle.MlPrediction == null)
            return null;

        var ml = bundle.MlPrediction;
        var hasMarket = bundle.MarketCalibrated != null;

        // ── Over 2.5 (ML-heavy) ──
        var (ep, emc, eml, emkt) = Effective(BttsPoisson, BttsMc, BttsMl, BttsMarket, hasMarket);
        var pOver = bundle.Poisson.Over25 * ep +
                    bundle.MonteCarlo.Over25 * emc +
                    GetYes(ml.Over25) * eml +
                    (bundle.MarketCalibrated?.Over25 ?? 0) * emkt;

        // ── BTTS (ML-heavy + goal correlation) ──
        var pBtts = bundle.Poisson.BTTS * ep +
                    bundle.MonteCarlo.BTTS * emc +
                    GetYes(ml.Btts) * eml +
                    (bundle.MarketCalibrated?.Btts ?? 0) * emkt;

        // Apply goal correlation adjustment (momentum effect)
        if (bundle.Poisson.IsValid)
        {
            pBtts = GoalCorrelation.AdjustBTTS(
                pBtts,
                bundle.Poisson.ExpectedHomeGoals,
                bundle.Poisson.ExpectedAwayGoals);
        }

        // ── 2-3 Goals (ML-heavy) ──
        var p23 = bundle.Poisson.TwoToThreeGoals * ep +
                  bundle.MonteCarlo.TwoToThreeGoals * emc +
                  GetYes(ml.Goals2To3) * eml;

        // ── Match Winner / HDA (MC-heavy) ──
        var (wp, wmc, wml, wmkt) = Effective(WinPoisson, WinMc, WinMl, WinMarket, hasMarket);

        var mlHda = ml.Hda.Probabilities;
        if (mlHda.Length < 3) mlHda = [0.33, 0.33, 0.33];

        var pHome = bundle.Poisson.HomeWin * wp +
                    bundle.MonteCarlo.HomeWin * wmc +
                    mlHda[0] * wml;

        var pDraw = bundle.Poisson.Draw * wp +
                    bundle.MonteCarlo.Draw * wmc +
                    mlHda[1] * wml;

        var pAway = bundle.Poisson.AwayWin * wp +
                    bundle.MonteCarlo.AwayWin * wmc +
                    mlHda[2] * wml;

        // Normalize HDA
        var total = pHome + pDraw + pAway;
        if (total > 0) { pHome /= total; pDraw /= total; pAway /= total; }

        // ── Gemini 40% Consensus Weighting ──
        if (!string.IsNullOrEmpty(geminiRecommendation) && geminiConfidence > 0)
        {
            double gConf = Math.Clamp(geminiConfidence.Value / 100.0, 0, 1);
            string rec = geminiRecommendation.Trim().ToLowerInvariant();

            if (rec.Contains("btts"))
            {
                pBtts = (pBtts * 0.6) + (gConf * 0.4);
            }
            else if (rec.Contains("over 2.5"))
            {
                pOver = (pOver * 0.6) + (gConf * 0.4);
            }
            else if (rec.Contains("under 2.5"))
            {
                pOver = (pOver * 0.6) + ((1.0 - gConf) * 0.4);
            }
            else if (rec.Contains("home"))
            {
                pHome = (pHome * 0.6) + (gConf * 0.4);
                pDraw *= 0.6;
                pAway *= 0.6;
                // Re-normalize HDA
                var hdaTotal = pHome + pDraw + pAway;
                if (hdaTotal > 0) { pHome /= hdaTotal; pDraw /= hdaTotal; pAway /= hdaTotal; }
            }
            else if (rec.Contains("away"))
            {
                pHome *= 0.6;
                pDraw *= 0.6;
                pAway = (pAway * 0.6) + (gConf * 0.4);
                // Re-normalize HDA
                var hdaTotal = pHome + pDraw + pAway;
                if (hdaTotal > 0) { pHome /= hdaTotal; pDraw /= hdaTotal; pAway /= hdaTotal; }
            }
        }

        // ── League volatility adjustment ──
        if (leagueId > 0)
        {
            pOver = volatility.AdjustProbability(leagueId, pOver);
            pBtts = volatility.AdjustProbability(leagueId, pBtts);
            p23 = volatility.AdjustProbability(leagueId, p23);
        }

        // ── H2H Divergence Boost (unlock missed value from fixture-specific patterns) ──
        if (h2h != null && h2h.IsValid)
        {
            pOver += H2HDivergenceBoost.Over25Boost(h2h, stats);
            pBtts += H2HDivergenceBoost.BTTSBoost(h2h, stats);
        }

        // ── Form Momentum Detection (catch teams trending in a new direction) ──
        pOver += FormMomentumDetector.Over25MomentumBoost(stats);
        pBtts += FormMomentumDetector.BTTSMomentumBoost(stats);
        
        // Attack momentum boosts both goal markets slightly
        var attackBoost = FormMomentumDetector.AttackMomentumBoost(stats);
        pOver += attackBoost * 0.5;
        pBtts += attackBoost * 0.3;

        var winner = "home";
        var confidence = pHome;

        if (pDraw > pHome && pDraw > pAway) { winner = "draw"; confidence = pDraw; }
        else if (pAway > pHome && pAway > pDraw) { winner = "away"; confidence = pAway; }

        return new WeightedPrediction
        {
            Over25 = pOver > 0.50,
            Over25Prob = Math.Clamp(pOver, 0, 1),
            BTTS = pBtts > 0.50,
            BTTSProb = Math.Clamp(pBtts, 0, 1),
            TwoToThreeGoals = p23 > 0.5,
            TwoToThreeGoalsProb = Math.Clamp(p23, 0, 1),
            MatchWinner = winner,
            Confidence = Math.Round(confidence, 2)
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static (double p, double mc, double ml, double mkt) Effective(
        double wP, double wMc, double wMl, double wMkt, bool hasMarket)
    {
        if (hasMarket) return (wP, wMc, wMl, wMkt);
        var redistrib = wMkt;
        var nonMarket = wP + wMc + wMl;
        return (wP + redistrib * wP / nonMarket,
                wMc + redistrib * wMc / nonMarket,
                wMl + redistrib * wMl / nonMarket,
                0);
    }

    private static double GetYes(MarketPrediction mp)
    {
        if (mp.Probabilities.Length > 1) return mp.Probabilities[1];
        if (mp.Confidence > 0) return mp.Prediction ? mp.Confidence : 1 - mp.Confidence;
        return 0.0;
    }
}
