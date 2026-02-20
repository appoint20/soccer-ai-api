using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Calibrates model probabilities against bookmaker odds.
/// Uses Shin method for true probability extraction + Bayesian blend.
/// Formula: final = 80% model + 20% Shin-adjusted market probability.
/// </summary>
public sealed class MarketCalibrationServiceImpl : IMarketCalibrationService
{
    private const double ModelWeight = 0.80;
    private const double MarketWeight = 0.20;

    public MarketCalibratedResult? Calibrate(
        MonteCarloModel model,
        double oddsOver25,
        double oddsBtts)
    {
        if (oddsOver25 <= 0 && oddsBtts <= 0)
            return null;

        // Use Shin method for true probabilities (removes bookmaker margin)
        var marketOver25 = oddsOver25 > 1
            ? ShinMarginRemoval.TrueProbability(oddsOver25, oddsOver25 > 0 ? 1.0 / (1.0 - 1.0 / oddsOver25) : 0)
            : model.Over25;

        var marketBtts = oddsBtts > 1
            ? ShinMarginRemoval.TrueProbability(oddsBtts, oddsBtts > 0 ? 1.0 / (1.0 - 1.0 / oddsBtts) : 0)
            : model.BTTS;

        return new MarketCalibratedResult
        {
            Over25 = Math.Clamp(model.Over25 * ModelWeight + marketOver25 * MarketWeight, 0, 1),
            Btts = Math.Clamp(model.BTTS * ModelWeight + marketBtts * MarketWeight, 0, 1)
        };
    }
}
