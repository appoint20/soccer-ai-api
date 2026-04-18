using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Orchestrates the synchronization of persisted AI fixture analyses.
/// </summary>
public interface IAiSyncService
{
    Task SyncUpcomingFixturesAsync(DateTime now, bool force = false, CancellationToken cancellationToken = default);
    Task SyncSingleFixtureAsync(int fixtureId, bool force = false, CancellationToken cancellationToken = default);
}
