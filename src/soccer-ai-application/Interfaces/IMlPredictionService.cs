using SoccerAi.Application.Features.Predictions;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Interface for ML prediction service.
/// </summary>
public interface IMlPredictionService
{
    Task<FixturePrediction?> PredictAsync(int fixtureId, CancellationToken ct = default);
    Task<Dictionary<string, double[]>> PredictFromFeaturesAsync(float[] features, CancellationToken ct = default);
}
