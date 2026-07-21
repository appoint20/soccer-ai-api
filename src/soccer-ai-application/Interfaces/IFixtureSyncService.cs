using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface IFixtureSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueFixturesAsync(int leagueId, int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct);
    Task<SyncResult> BackfillEloAsync(CancellationToken ct);

    /// <summary>
    /// Timestamped odds captures for upcoming in-scope fixtures:
    /// first availability, then refresh snapshots at T-24h and T-1h.
    /// Returns the number of fixtures captured.
    /// </summary>
    Task<int> CaptureUpcomingOddsAsync(CancellationToken ct);
}
