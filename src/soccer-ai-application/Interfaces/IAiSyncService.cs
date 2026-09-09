using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Orchestrates the synchronization of persisted AI fixture analyses.
/// </summary>
public interface IAiSyncService
{
    /// <summary>
    /// Generates narratives for fixtures that have not kicked off yet, from
    /// <paramref name="now"/> out to <paramref name="daysAhead"/> days.
    /// </summary>
    Task SyncUpcomingFixturesAsync(
        DateTime now,
        bool force = false,
        int daysAhead = 5,
        CancellationToken cancellationToken = default);
    Task SyncSingleFixtureAsync(int fixtureId, bool force = false, CancellationToken cancellationToken = default);
}
