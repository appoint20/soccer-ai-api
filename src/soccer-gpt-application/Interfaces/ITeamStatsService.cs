using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ITeamStatsService
{
    Task<TeamAggregatedStats> CalculateAsync(
        string teamName,
        List<Match> matches,
        TeamStatsOptions options);
}
