
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ILocalTeamStatsRepository
{
    Task<TeamStatsData?> GetTeamStatsByNameAsync(string leagueName, string teamName, CancellationToken cancellationToken);
}
