using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class ScheduledJobService : IScheduledJobService
{
    private readonly ILogger<ScheduledJobService> _logger;
    private readonly IFootballApiService _footballApiService;

    public ScheduledJobService(ILogger<ScheduledJobService> logger, IFootballApiService footballApiService)
    {
        _logger = logger;
        _footballApiService = footballApiService;
    }

    public async Task ExecuteDailyJobAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing daily API job at: {time}", DateTimeOffset.Now);
        
        // Example: Fetch Man City stats (Team 50) for Premier League (League 39) Season 2025
        var stats = await _footballApiService.GetTeamStatsAsync(39, 50, 2025, cancellationToken);
        
        if (stats != null)
        {
            _logger.LogInformation("Fetched stats for {Team}. Form: {Form}. Goals For: {GoalsFor}", 
                stats.Team?.Name, stats.Form, stats.Goals?.For?.Total?.Total);
        }
        else
        {
            _logger.LogWarning("Failed to fetch stats.");
        }
    }
}
