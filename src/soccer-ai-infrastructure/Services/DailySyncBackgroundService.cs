using Mediator.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Features.Automation;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// A perfectly efficient background service that sleeps until exactly 03:35 AM
/// and triggers the RunDailySyncCommand. Uses zero CPU while waiting.
/// </summary>
public class DailySyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<DailySyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Daily Sync Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRunTime = new DateTime(now.Year, now.Month, now.Day, 3, 0, 0);

            // If it's already past 3:00 AM today, schedule for 3:00 AM tomorrow.
            if (now > nextRunTime)
            {
                nextRunTime = nextRunTime.AddDays(1);
            }

            var delay = nextRunTime - now;
            logger.LogInformation("Next Daily Sync is scheduled for: {NextRunTime} (in {DelayHours} hours, {DelayMinutes} minutes)", 
                nextRunTime.ToString("yyyy-MM-dd HH:mm:ss"), delay.Hours, delay.Minutes);

            try
            {
                // Sleep with zero CPU usage until 03:35 AM
                await Task.Delay(delay, stoppingToken);

                // We've woken up! Trigger the sync.
                logger.LogInformation("Waking up at {Time}! Executing Daily Sync Orchestration...", DateTime.Now);

                using var scope = serviceProvider.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                await mediator.SendAsync(new RunDailySyncCommand(DateTime.Now.Year), stoppingToken);

                logger.LogInformation("Daily Sync Orchestration completed successfully. Going back to sleep.");
            }
            catch (TaskCanceledException)
            {
                // Expected when application shuts down
                logger.LogInformation("Background service sleep was cancelled gracefully.");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue loop so the service doesn't crash permanently
                logger.LogError(ex, "A fatal error occurred during the scheduled Daily Sync.");
                
                // Wait 5 minutes before trying the loop again to avoid spamming logs on failure
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        logger.LogInformation("Daily Sync Background Service is stopping.");
    }
}
