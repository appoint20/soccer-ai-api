using SoccerAi.Application.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Worker;

/// <summary>
/// Independent odds capture loop: the configured three-hour refresh, with
/// more frequent checks in the final approach to kickoff. All times UTC.
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

                // Odds first, deliberately. They decide whether a fixture can be
                // priced at all; absences only refine a price that already
                // exists, so they take what budget is left rather than
                // competing for it.
                await syncService.CaptureUpcomingInjuriesAsync(stoppingToken);

                // Last, and cheapest over time: a pairing is asked about once
                // and the answer never expires, so this settles to zero calls
                // once the board is covered.
                await syncService.CaptureHeadToHeadAsync(stoppingToken);
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
