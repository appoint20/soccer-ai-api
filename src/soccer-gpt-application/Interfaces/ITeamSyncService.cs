using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ITeamSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueStandingsAsync(int leagueId, int season, CancellationToken cancellationToken);
}
