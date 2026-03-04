using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface IFixtureSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueFixturesAsync(int leagueId, int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct);
    Task<SyncResult> BackfillEloAsync(CancellationToken ct);
}
