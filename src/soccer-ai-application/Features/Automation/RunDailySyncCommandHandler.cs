using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Features.Backtesting;

namespace SoccerAi.Application.Features.Automation;

public class RunDailySyncCommandHandler(
    ITeamSyncService teamSyncService, IFixtureSyncService fixtureSyncService,
    IMlTrainingService mlTrainingService, IAiSyncService aiSyncService, 
    IMediator mediator,
    ILogger<RunDailySyncCommandHandler> logger) 
    : ICommandHandler<RunDailySyncCommand>
{
    public async Task Handle(IReceiveContext<RunDailySyncCommand> context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Orchestrating daily sync for season {Season}", context.Message.Season);

        try
        {
            // 1. Sync Standings
            await teamSyncService.SyncAllLeaguesAsync(context.Message.Season, cancellationToken);

            // 2. Sync Fixtures
            await fixtureSyncService.SyncAllLeaguesAsync(context.Message.Season, cancellationToken);

            // 3. Train ML Models natively via ML.NET
            await mlTrainingService.TrainModelsAsync(cancellationToken);

            // 4. Generate AI Analysis
            await aiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, false, cancellationToken);

            // 5. Weekly Persistence: Run Backtest Simulation (Mondays) to refresh the cache
            if (DateTime.Today.DayOfWeek == DayOfWeek.Monday)
            {
                logger.LogInformation("Monday detected: Triggering weekly backtest report refresh...");
                // We use mediator to call the query handler directly to trigger calculation and save to DB
                // This ensures the user gets an instant response throughout the week
                await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(
                    new GetBacktestReportQuery(WeeksBack: 10, Stake: 100.0, Refresh: true), cancellationToken);
            }

            logger.LogInformation("Daily sync orchestration completed successfully.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Daily sync orchestration was gracefully interrupted by application shutdown.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily sync orchestration failed: {Message}", ex.Message);
            throw;
        }
    }
}
