using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IFootballApiService
{
    Task<TeamStatsData?> GetTeamStatsAsync(int leagueId, int teamId, int season, CancellationToken cancellationToken);
}
