using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ILeagueStatsService
{
    Task<LeagueGoalAverages> CalculateLeagueAveragesAsync(string league, IOrderedQueryable<Match> matches);
}
