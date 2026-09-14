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
    /// <summary>
    /// Generates narratives for every fixture on one UTC calendar day that has
    /// not kicked off, and reports the whole day — including what was not
    /// attempted and why.
    /// </summary>
    /// <remarks>
    /// Per-fixture failures are reported, not thrown, so the caller can say
    /// which fixtures to retry. A rejected or missing provider credential still
    /// throws: no fixture can succeed, and repeating the request per fixture
    /// would only repeat the rejection.
    /// </remarks>
    Task<AiSyncReport> SyncDateAsync(DateOnly dateUtc, bool force = false, CancellationToken cancellationToken = default);

    Task SyncSingleFixtureAsync(int fixtureId, bool force = false, CancellationToken cancellationToken = default);
}
