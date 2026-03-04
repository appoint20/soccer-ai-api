using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface ITeamSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueStandingsAsync(int leagueId, int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncMultipleSeasonsAsync(int numberOfSeasons, CancellationToken ct);
}
