using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IFootballApiService
{
    Task<TeamStatsData?> GetTeamStatsAsync(int leagueId, int teamId, int season, CancellationToken cancellationToken);
    Task<List<ApiFixture>?> GetFixturesAsync(int leagueId, int season, int next, CancellationToken cancellationToken);
    Task<ApiFootballPrediction?> GetPredictionAsync(int fixtureId, CancellationToken cancellationToken);
    
    // Schedule & History
    // Combined call for efficiency (impl should handle parallel calls or caching)
    Task<List<ApiFixture>?> GetTeamFixturesAsync(int teamId, int last = 5, int next = 2, CancellationToken cancellationToken = default);
}
