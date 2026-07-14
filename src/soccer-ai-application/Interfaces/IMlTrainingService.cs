namespace SoccerAi.Application.Interfaces;

public interface IMlTrainingService
{
    /// <summary>Train with the default temporal cutoff (newest 20% of rows held out).</summary>
    Task TrainModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Train with an explicit temporal cutoff: rows before it train, rows on or
    /// after it are the held-out test set. Choose a cutoff at or before the
    /// backtest window start so backtest and training data never overlap.
    /// </summary>
    Task TrainModelsAsync(DateTimeOffset? temporalCutoff, CancellationToken ct = default);
}
