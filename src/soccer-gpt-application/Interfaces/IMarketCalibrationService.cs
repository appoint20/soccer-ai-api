using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Calibrates model probabilities against bookmaker odds using Bayesian update.
/// Produces a separate MarketCalibratedResult that the consensus engine blends.
/// </summary>
public interface IMarketCalibrationService
{
    MarketCalibratedResult? Calibrate(
        MonteCarloModel model,
        double oddsOver25,
        double oddsBtts);
}

/// <summary>
/// Market-calibrated probabilities: 80% model + 20% market implied.
/// </summary>
public sealed class MarketCalibratedResult
{
    public double Over25 { get; init; }
    public double Btts { get; init; }
}
