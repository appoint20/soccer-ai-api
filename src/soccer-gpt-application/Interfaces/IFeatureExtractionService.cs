using soccer_gpt_application.Entities;

namespace soccer_gpt_application.Interfaces;

public interface IFeatureExtractionService
{
    Task<float[]> BuildFeaturesAsync(Fixture fixture, CancellationToken ct);
}
