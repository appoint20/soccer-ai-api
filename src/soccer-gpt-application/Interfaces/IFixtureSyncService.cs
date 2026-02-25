using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IFixtureSyncService
{
    Task<SyncResult> SyncAllLeaguesAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> SyncLeagueFixturesAsync(int leagueId, int season, CancellationToken cancellationToken);
}
