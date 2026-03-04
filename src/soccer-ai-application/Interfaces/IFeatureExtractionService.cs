using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Interfaces;

public interface IFeatureExtractionService
{
    Task<float[]> BuildFeaturesAsync(Fixture fixture, CancellationToken ct);
}
