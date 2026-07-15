using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Computes the full analysis for a fixture and persists it as a snapshot in
/// FixtureAnalysis, so the HTTP read path never runs models. Used by the sync
/// agent (recompute trigger) and as a fallback when a snapshot is missing.
/// </summary>
public interface IAnalysisPrecomputeService
{
    /// <summary>
    /// Recompute and persist snapshots for one fixture (all languages).
    /// Returns the freshly mapped responses keyed by language code.
    /// </summary>
    Task<IReadOnlyDictionary<string, MatchAnalysis>> RecomputeFixtureAsync(
        int fixtureId, CancellationToken ct = default);

    /// <summary>Recompute snapshots for every fixture in the date window.</summary>
    Task<int> RecomputeWindowAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default);
}
