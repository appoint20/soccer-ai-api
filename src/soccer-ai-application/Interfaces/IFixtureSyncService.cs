using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface IFixtureSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueFixturesAsync(int leagueId, int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct);

    /// <summary>
    /// Pulls prior seasons for any league that does not yet hold enough finished
    /// fixtures for the model to run.
    ///
    /// The daily sync only ever fetches the current season, so on the day a new
    /// season starts a league has zero finished fixtures — the model returns
    /// null, and the absence travels silently all the way to an empty app. This
    /// closes that gap and is a no-op once each league has depth.
    /// </summary>
    Task<SyncResult> EnsureHistoricalDepthAsync(
        int season, int minFinishedPerLeague, int maxSeasonsBack, CancellationToken ct);
    Task<SyncResult> BackfillEloAsync(CancellationToken ct);

    /// <summary>
    /// Timestamped odds captures for upcoming in-scope fixtures:
    /// first availability, then refresh snapshots at T-24h and T-1h.
    /// Returns the number of fixtures captured.
    /// </summary>
    Task<int> CaptureUpcomingOddsAsync(CancellationToken ct);
}
