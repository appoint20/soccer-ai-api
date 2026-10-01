using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Sync;

namespace SoccerAi.Worker;

/// <summary>
/// Keeps in-play scores and the match minute current.
/// </summary>
/// <remarks>
/// The sync step asks the database before the provider, so outside the hours
/// our own fixtures are being played this loop wakes, finds nothing running and
/// spends no request at all.
/// </remarks>
public sealed class LiveScoreWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    ILogger<LiveScoreWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.LiveScoreIntervalSeconds;
        if (interval <= 0)
        {
            logger.LogInformation("Live score worker disabled (LiveScoreIntervalSeconds = {Interval})", interval);
            return;
        }

        logger.LogInformation("Live score worker starting (every {Interval}s while matches are in play)", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IFixtureSyncService>();
                await syncService.CaptureLiveScoresAsync(stoppingToken);

                // Statistics cost a request per twenty fixtures, so they follow
                // only the matches we published a pick on, and only when their
                // last reading has gone stale.
                await syncService.CaptureLiveStatsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Live score refresh failed — retrying next tick");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
