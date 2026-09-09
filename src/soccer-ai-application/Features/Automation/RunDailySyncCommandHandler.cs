using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Features.Backtesting;

namespace SoccerAi.Application.Features.Automation;

public class RunDailySyncCommandHandler(
    ITeamSyncService teamSyncService, IFixtureSyncService fixtureSyncService,
    IMlTrainingService mlTrainingService, IGoalRateTrainingService goalRateTrainingService,
    IAiSyncService aiSyncService,
    IAnalysisPrecomputeService precomputeService,
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

            // 3. Retrain the hybrid goal-rate model.
            //
            // This runs BEFORE the precompute below, so the day's snapshots are
            // built from a model trained on results up to yesterday. Order
            // matters: retraining afterwards would leave the published board a
            // day behind the model that measured it.
            //
            // A training failure must not abort the sync — fixtures, odds and
            // snapshots are all still worth refreshing, and the forecaster keeps
            // serving the previous model until a run succeeds.
            try
            {
                await goalRateTrainingService.TrainAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Goal-rate training failed; continuing with the previously trained model");
            }

            // 3b. Legacy per-market binary trainer. Nothing loads its output —
            // kept only so existing tooling and reports do not break.
            await mlTrainingService.TrainModelsAsync(cancellationToken);

            // 4. Generate AI Analysis
            await aiSyncService.SyncUpcomingFixturesAsync(
                DateTime.UtcNow, force: false, cancellationToken: cancellationToken);

            // 5. Precompute analysis snapshots so GET /api/analyze is a pure DB read
            var nowUtc = DateTimeOffset.UtcNow;
            await precomputeService.RecomputeWindowAsync(
                nowUtc.Date.AddDays(-3), nowUtc.Date.AddDays(4), cancellationToken);

            // 6. Weekly Persistence: Run Backtest Simulation (Mondays) to refresh the cache
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
