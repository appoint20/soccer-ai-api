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

    /// <summary>
    /// Captures reported absences for fixtures approaching kickoff. Returns how
    /// many fixtures were fetched.
    /// </summary>
    Task<int> CaptureUpcomingInjuriesAsync(CancellationToken ct);

    /// <summary>
    /// Fetches past meetings for upcoming fixtures whose pairing has not been
    /// asked about yet. Returns how many fixtures were checked.
    /// </summary>
    Task<int> CaptureHeadToHeadAsync(CancellationToken ct);

    /// <summary>
    /// Fetches the provider's own prediction for upcoming fixtures. Returns how
    /// many fixtures were fetched.
    /// </summary>
    Task<int> CapturePredictionsAsync(CancellationToken ct);

    /// <summary>
    /// Refreshes the score and minute of every fixture of ours in play, from a
    /// single request. Returns how many were updated.
    /// </summary>
    Task<int> CaptureLiveScoresAsync(CancellationToken ct);

    /// <summary>
    /// Refreshes match statistics for in-play fixtures carrying a published
    /// pick. Returns how many were updated.
    /// </summary>
    Task<int> CaptureLiveStatsAsync(CancellationToken ct);
}
