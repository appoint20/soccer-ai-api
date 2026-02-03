using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Background service that syncs teams and fixtures at 03:00 AM daily.
/// </summary>
public class DataSyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<DataSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan SyncTime = new(03, 00, 0); // 03:00 AM

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Standings sync background service started. Will sync at {Time} daily", SyncTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var nextRun = CalculateNextRun(now);
                var delay = nextRun - now;

                logger.LogInformation("Next standings sync scheduled for {NextRun} (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                    await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Standings sync background service stopping");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in standings sync background service");
                // Wait 1 hour before retrying on error
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting nightly sync at {Time}", DateTime.Now);

        using var scope = serviceProvider.CreateScope();
        var currentSeason = DateTime.Now.Month >= 7 ? DateTime.Now.Year : DateTime.Now.Year - 1;

        // 1. Sync Teams (Standings) first
        try 
        {
            var teamService = scope.ServiceProvider.GetRequiredService<TeamSyncService>();
            var teamResult = await teamService.SyncAllLeaguesAsync(currentSeason, cancellationToken);
            logger.LogInformation(
                "Team sync completed: {Leagues} leagues, {Created} created, {Updated} updated, {Errors} errors",
                teamResult.LeaguesSynced, teamResult.Created, teamResult.Updated, teamResult.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Team sync");
        }

        // 2. Then sync Fixtures
        try
        {
            var fixtureService = scope.ServiceProvider.GetRequiredService<FixtureSyncService>();
            var fixtureResult = await fixtureService.SyncAllLeaguesAsync(currentSeason, cancellationToken);
            logger.LogInformation(
                "Fixture sync completed: {Leagues} leagues, {Created} created, {Updated} updated, {Errors} errors",
                fixtureResult.LeaguesSynced, fixtureResult.Created, fixtureResult.Updated, fixtureResult.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Fixture sync");
        }
    }

    private static DateTime CalculateNextRun(DateTime now)
    {
        var today = now.Date;
        var todaySyncTime = today.Add(SyncTime);

        // If we haven't passed today's sync time, use today
        // Otherwise, use tomorrow
        return now < todaySyncTime ? todaySyncTime : todaySyncTime.AddDays(1);
    }
}
