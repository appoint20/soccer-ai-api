using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Automation;

public class RunDailySyncCommandHandler(
    ITeamSyncService teamSyncService, IFixtureSyncService fixtureSyncService,
    IMlTrainingService mlTrainingService, IGeminiSyncService geminiSyncService, 
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

            // 4. Generate Gemini AI Analysis
            await geminiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, cancellationToken);

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
