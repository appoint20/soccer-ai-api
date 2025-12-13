using soccer_gpt_application.Interfaces;

namespace soccer_gpt_worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    // Time to run: 08:00 AM
    private readonly TimeSpan _scheduledTime = new(8, 0, 0);

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker Service started. Scheduled time: {time}", _scheduledTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.Add(_scheduledTime);
            if (now > nextRun)
            {
                nextRun = nextRun.AddDays(1);
            }

            var delay = nextRun - now;
            _logger.LogInformation("Waiting {delay} until next execution at {nextRun}", delay, nextRun);

            try
            {
                await Task.Delay(delay, stoppingToken);
                
                using (var scope = _serviceProvider.CreateScope())
                {
                    var jobService = scope.ServiceProvider.GetRequiredService<IScheduledJobService>();
                    await jobService.ExecuteDailyJobAsync(stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing scheduled job");
            }
        }
    }
}
