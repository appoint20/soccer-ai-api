using SoccerAi.Application.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Worker;

/// <summary>
/// Lightweight interval loop for T-schedule odds captures (first availability,
/// T-24h, T-1h). Runs independently of the twice-daily full sync so line
/// movement close to kickoff is not missed. All times UTC.
/// </summary>
public sealed class OddsCaptureWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    ILogger<OddsCaptureWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = options.Value.OddsCaptureIntervalMinutes;
        if (intervalMinutes <= 0)
        {
            logger.LogInformation("Odds capture worker disabled (interval {Minutes} ≤ 0)", intervalMinutes);
            return;
        }

        logger.LogInformation("Odds capture worker starting (every {Minutes} min)", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IFixtureSyncService>();
                await syncService.CaptureUpcomingOddsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Odds capture run failed — retrying next interval");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Odds capture worker stopping.");
    }
}
