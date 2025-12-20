using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ITeamStatsService
{
    Task<RichTeamStatsDto> CalculateStatsAsync(string teamName, List<HistoricalMatchDto> history);
}


