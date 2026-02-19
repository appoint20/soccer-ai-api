using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Calibrates model probabilities against bookmaker odds.
/// Formula: final = 80% model + 20% market implied probability.
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

        var marketOver25 = oddsOver25 > 0 ? ImpliedProbability(oddsOver25) : model.Over25;
        var marketBtts = oddsBtts > 0 ? ImpliedProbability(oddsBtts) : model.BTTS;

        return new MarketCalibratedResult
        {
            Over25 = Math.Clamp(model.Over25 * ModelWeight + marketOver25 * MarketWeight, 0, 1),
            Btts = Math.Clamp(model.BTTS * ModelWeight + marketBtts * MarketWeight, 0, 1)
        };
    }

    /// <summary>Convert decimal odds to implied probability.</summary>
    private static double ImpliedProbability(double odds)
        => odds > 1 ? 1.0 / odds : 0;
}
