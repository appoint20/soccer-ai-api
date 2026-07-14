using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Runs the single probability flow:
/// Dixon-Coles model → market calibration. Nothing else.
/// </summary>
public interface IProbabilityPipeline
{
    Task<ProbabilityBundle?> RunAsync(Fixture fixture, TeamStatsResponse stats, CancellationToken ct);
}

/// <summary>
/// Output of the probability flow. <see cref="Calibrated"/> is the only
/// probability set decisions may consume; <see cref="Poisson"/> is kept for
/// diagnostics and trap detection (raw model vs market divergence).
/// </summary>
public sealed class ProbabilityBundle
{
    public required PoissonModel Poisson { get; init; }
    public required CalibratedProbabilities Calibrated { get; init; }
}
