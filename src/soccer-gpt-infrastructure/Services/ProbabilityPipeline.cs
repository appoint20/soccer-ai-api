using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Runs all probability models (Poisson → Monte Carlo → ML) in sequence.
/// Single place where all models execute — MatchAnalysisService never calls them directly.
/// </summary>
public sealed class ProbabilityPipeline(
    IPoissonCalculationService poissonService,
    IMonteCarloService monteCarloService,
    IMlPredictionService mlService,
    IFeatureExtractionService featureService,
    IMarketCalibrationService marketCalibrationService,
    ILogger<ProbabilityPipeline> logger) : IProbabilityPipeline
{
    public async Task<ProbabilityBundle> RunAsync(
        Fixture fixture,
        TeamStatsResponse stats,
        CancellationToken ct)
    {
        // ── 1. Poisson — attack/defense strength relative to league ──
        var poissonProbs = await poissonService.CalculateProbabilitiesAsync(
            fixture.LeagueId, fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, ct);

        var poissonModel = poissonProbs != null ? new PoissonModel
        {
            ExpectedHomeGoals = poissonProbs.HomeExpectedGoals,
            ExpectedAwayGoals = poissonProbs.AwayExpectedGoals,
            ExpectedScoreDifference = poissonProbs.HomeExpectedGoals - poissonProbs.AwayExpectedGoals,
            HomeWin = poissonProbs.HomeWin,
            Draw = poissonProbs.Draw,
            AwayWin = poissonProbs.AwayWin,
            BTTS = poissonProbs.BothTeamScoredGoal,
            Over25 = poissonProbs.Over25,
            TwoToThreeGoals = poissonProbs.TwoToThreeGoals,
            IsValid = true
        } : PoissonModel.Empty;

        // ── 2. Monte Carlo — uses λ from Poisson + market calibration ──
        var marketOdds = (fixture.BttsYesOdds.HasValue || fixture.Over25Odds.HasValue)
            ? new MarketOdds
            {
                BttsOdds = fixture.BttsYesOdds ?? 0,
                Over25Odds = fixture.Over25Odds ?? 0
            }
            : null;

        var mcResult = poissonProbs != null
            ? monteCarloService.Predict(poissonProbs, marketOdds)
            : null;

        var monteCarloModel = mcResult != null ? new MonteCarloModel
        {
            SimulationCount = 50000,
            HomeWin = mcResult.MonteCarlo.HomeWinProbability,
            Draw = mcResult.MonteCarlo.DrawProbability,
            AwayWin = mcResult.MonteCarlo.AwayWinProbability,
            BTTS = mcResult.MonteCarlo.BttsProbability,
            Over25 = mcResult.MonteCarlo.Over25Probability,
            TwoToThreeGoals = mcResult.MonteCarlo.TwoToThreeGoalsProbability
        } : MonteCarloModel.Empty;

        // Calibrated values (Bayesian update of MC + market odds)
        var calibratedBtts = mcResult?.FinalBttsProbability;
        var calibratedOver25 = mcResult?.FinalOver25Probability;

        // ── 3. ML Prediction ──
        FixturePrediction? mlPrediction = null;
        try
        {
            var features = await featureService.BuildFeaturesAsync(fixture, ct);
            var mlResults = await mlService.PredictFromFeaturesAsync(features, ct);

            if (mlResults.Count > 0)
            {
                mlPrediction = new FixturePrediction(
                    fixture.Id,
                    BuildMarketPrediction("Over 2.5 Goals", mlResults.GetValueOrDefault("over25")),
                    BuildMarketPrediction("Both Teams To Score", mlResults.GetValueOrDefault("btts")),
                    BuildMarketPrediction("2-3 Goals", mlResults.GetValueOrDefault("goals_2_3")),
                    BuildMarketPrediction("Match Winner", mlResults.GetValueOrDefault("hda"))
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ML prediction failed for fixture {Id}", fixture.Id);
        }

        // ── 4. Market calibration (80% model + 20% market implied) ──
        var marketCalibrated = marketCalibrationService.Calibrate(
            monteCarloModel,
            fixture.Over25Odds ?? 0,
            fixture.BttsYesOdds ?? 0);

        return new ProbabilityBundle
        {
            Poisson = poissonModel,
            MonteCarlo = monteCarloModel,
            MlPrediction = mlPrediction,
            CalibratedBttsProb = calibratedBtts,
            CalibratedOver25Prob = calibratedOver25,
            MarketCalibrated = marketCalibrated
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static MarketPrediction BuildMarketPrediction(string market, double[]? probs)
    {
        if (probs == null || probs.Length < 2)
            return new MarketPrediction(market, false, 0, []);

        if (market == "Match Winner" && probs.Length >= 3)
        {
            var maxIdx = Array.IndexOf(probs, probs.Max());
            return new MarketPrediction(market, true, probs[maxIdx], probs);
        }

        var yesProb = probs.Length > 1 ? probs[1] : probs[0];
        return new MarketPrediction(market, yesProb > 0.45, yesProb > 0.5 ? yesProb : 1 - yesProb, probs);
    }
}
