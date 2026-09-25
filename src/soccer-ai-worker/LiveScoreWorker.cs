using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Sync;

namespace SoccerAi.Worker;

/// <summary>
/// Keeps in-play scores and the match minute current.
/// </summary>
/// <remarks>
/// Separate from the odds loop because it needs a completely different cadence:
/// a price is worth re-reading every half hour, a score every minute. One
/// request returns every match in play anywhere, so the cost is the tick rate
/// and nothing else — it does not grow with the size of the matchday.
///
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
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A live score is the most disposable thing this service holds:
                // the next tick is a minute away, so nothing is retried here.
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
