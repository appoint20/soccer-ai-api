namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Trains the hybrid model's two goal-rate regressors and writes an evaluation
/// report next to them.
/// </summary>
/// <remarks>
/// Unlike the per-market binary trainer this replaces, the artefacts written
/// here are actually consumed — <see cref="IGoalRateForecaster"/> loads them.
/// </remarks>
public interface IGoalRateTrainingService
{
    Task TrainAsync(CancellationToken ct = default);
}
