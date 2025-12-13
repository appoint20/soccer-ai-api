using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class ScheduledJobService : IScheduledJobService
{
    private readonly ILogger<ScheduledJobService> _logger;
    private readonly HttpClient _httpClient;

    public ScheduledJobService(ILogger<ScheduledJobService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task ExecuteDailyJobAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing daily API job at: {time}", DateTimeOffset.Now);
        
        // Example API call integration
        // await _httpClient.GetAsync("https://api.example.com/daily-update", cancellationToken);
        
        await Task.CompletedTask;
    }
}
