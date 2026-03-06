using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Orchestrates the synchronization of Gemini AI fixture analyses.
/// </summary>
public interface IGeminiSyncService
{
    Task SyncUpcomingFixturesAsync(DateTime now, CancellationToken cancellationToken = default);
    Task SyncSingleFixtureAsync(int fixtureId, CancellationToken cancellationToken = default);
}
