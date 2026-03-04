using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Runs all probability models (Poisson, Monte Carlo, ML) in sequence
/// and returns a unified probability bundle.
/// </summary>
public interface IProbabilityPipeline
{
    Task<ProbabilityBundle> RunAsync(Fixture fixture, TeamStatsResponse stats, CancellationToken ct);
}

/// <summary>
/// Container for all model outputs — one place to access every model result.
/// </summary>
public sealed class ProbabilityBundle
{
    public required PoissonModel Poisson { get; init; }
    public required MonteCarloModel MonteCarlo { get; init; }
    public FixturePrediction? MlPrediction { get; init; }

    /// <summary>Market-calibrated BTTS probability (Bayesian update of MC + odds). Null if no odds available.</summary>
    public double? CalibratedBttsProb { get; init; }

    /// <summary>Market-calibrated Over 2.5 probability (Bayesian update of MC + odds). Null if no odds available.</summary>
    public double? CalibratedOver25Prob { get; init; }

    /// <summary>Standalone market calibration result (80% model + 20% market). Used by consensus engine.</summary>
    public MarketCalibratedResult? MarketCalibrated { get; init; }
}
