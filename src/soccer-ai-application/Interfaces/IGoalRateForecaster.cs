using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Probabilities produced by the hybrid model: two learned goal rates pushed
/// through the Dixon-Coles score matrix.
/// </summary>
/// <param name="LambdaHome">Learned expected home goals.</param>
/// <param name="LambdaAway">Learned expected away goals.</param>
public sealed record GoalRateForecast(
    double LambdaHome,
    double LambdaAway,
    PoissonProbabilities Probabilities,
    string? ModelVersion = null);

/// <summary>
/// Scores fixtures with the trained goal-rate models.
///
/// This is the half of the ML stack that was previously absent: the training
/// service wrote model files that nothing ever read, so the machine-learning
/// component contributed nothing to a single published prediction. An
/// implementation of this interface is what makes a trained model reachable
/// from the probability pipeline.
/// </summary>
public interface IGoalRateForecaster
{
    /// <summary>True when both goal-rate models are loaded and usable.</summary>
    bool IsAvailable { get; }
    string? ModelVersion => null;

    /// <summary>
    /// Forecast one fixture. Returns null when the models are unavailable or
    /// the fixture cannot be featurised — callers fall back to classic
    /// Dixon-Coles rather than receiving a guess.
    /// </summary>
    Task<GoalRateForecast?> ForecastAsync(Fixture fixture, CancellationToken ct = default);

    /// <summary>
    /// Forecast several fixtures sharing one history pass. Far cheaper than
    /// calling <see cref="ForecastAsync"/> per fixture, which rebuilds the
    /// rolling feature state from scratch each time.
    /// </summary>
    Task<IReadOnlyDictionary<int, GoalRateForecast>> ForecastManyAsync(
        IReadOnlyCollection<Fixture> fixtures, CancellationToken ct = default);
}
