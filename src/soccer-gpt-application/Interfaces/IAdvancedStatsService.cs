using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IAdvancedStatsService
{
    Task<PoissonProbabilitiesDto> CalculateAnalyticsAsync(
        string homeTeam, 
        string awayTeam, 
        List<HistoricalMatchDto> allHistory,
        string? league = null);
}

