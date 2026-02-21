using soccer_gpt_application.Features.Predictions;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Interface for ML prediction service.
/// </summary>
public interface IMlPredictionService
{
    Task<FixturePrediction?> PredictAsync(int fixtureId, CancellationToken ct = default);
    Task<Dictionary<string, double[]>> PredictFromFeaturesAsync(float[] features, CancellationToken ct = default);
}
