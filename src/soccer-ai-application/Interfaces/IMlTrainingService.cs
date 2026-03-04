namespace SoccerAi.Application.Interfaces;

public interface IMlTrainingService
{
    Task TrainModelsAsync(CancellationToken ct = default);
}
