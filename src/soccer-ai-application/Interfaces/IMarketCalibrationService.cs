using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// The single calibration step between the Dixon-Coles model and every consumer:
/// final_p = w_model × p_DC + w_market × p_market, where p_market comes from
/// Shin-margin-removed bookmaker odds. No other blending exists anywhere.
/// </summary>
public interface IMarketCalibrationService
{
    /// <summary>
    /// Calibrate raw Dixon-Coles probabilities against the fixture's odds.
    /// Markets without usable odds keep the pure model probability.
    /// </summary>
    CalibratedProbabilities Calibrate(PoissonProbabilities model, Fixture fixture);
}

/// <summary>
/// Final calibrated probabilities — the ONLY probability set the decision
/// layer is allowed to consume.
/// </summary>
public sealed class CalibratedProbabilities
{
    public double HomeWin { get; init; }
    public double Draw { get; init; }
    public double AwayWin { get; init; }
    public double Over25 { get; init; }
    public double Btts { get; init; }
    public double TwoToThreeGoals { get; init; }

    /// <summary>True when at least one market was blended with real odds.</summary>
    public bool UsedMarketOdds { get; init; }
}
