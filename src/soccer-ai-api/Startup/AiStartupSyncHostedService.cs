using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Api.Startup;

/// <summary>
/// Background hosted service that runs on application startup to ensure
/// today's and the upcoming 3 days' fixtures have AI match analysis generated.
/// </summary>
public sealed class AiStartupSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiStartupSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay so web server initialization and migrations finish first
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logger.LogInformation("[Startup] Starting background AI analysis sync for today and the upcoming 3 days...");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var aiSyncService = scope.ServiceProvider.GetRequiredService<IAiSyncService>();

            // Runs upcoming fixtures analysis (today + upcoming 3 days)
            await aiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, force: false, stoppingToken);

            logger.LogInformation("[Startup] Background AI analysis sync completed successfully.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[Startup] Background AI analysis sync was cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Startup] Background AI analysis sync encountered an error.");
        }
    }
}
