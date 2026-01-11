using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ITeamAnalyticsService
{
    TeamPerformanceStats CalculateStats(List<HistoricalMatchDto> matches, string teamName);
}
