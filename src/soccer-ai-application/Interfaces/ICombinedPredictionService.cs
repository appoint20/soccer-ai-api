using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface ICombinedPredictionService
{
    CombinedPrediction Combine(int fixtureId, DateTimeOffset kickoff, DateTimeOffset capturedAt,
        IReadOnlyList<PredictionSource> sources);
}

public interface ICombinedPredictionRefreshService
{
    Task<CombinedPredictionRefreshResult> RefreshAsync(int fixtureId, bool refreshNarration = true,
        CancellationToken ct = default);
}
