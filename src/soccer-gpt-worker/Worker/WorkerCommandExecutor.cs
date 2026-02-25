using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_worker.Worker;

public sealed class WorkerCommandExecutor(
    ISyncJobRunner syncJobRunner,
    ILogger<WorkerCommandExecutor> logger)
{
    public async Task<int> ExecuteAsync(WorkerCommand command, CancellationToken cancellationToken)
    {
        var season = command.Season ?? GetCurrentSeason();

        try
        {
            switch (command.Job)
            {
                case WorkerJob.Standings:
                    await syncJobRunner.RunStandingsAsync(season, cancellationToken);
                    break;
                case WorkerJob.Fixtures:
                    await syncJobRunner.RunFixturesAsync(season, cancellationToken);
                    break;
                case WorkerJob.Gemini:
                    await syncJobRunner.RunGeminiAsync(cancellationToken);
                    break;
                case WorkerJob.Ml:
                    var trainingOk = await syncJobRunner.RunMlTrainingAsync(cancellationToken);
                    return trainingOk ? 0 : 1;
                case WorkerJob.Nightly:
                    await syncJobRunner.RunNightlyAsync(season, cancellationToken);
                    break;
                default:
                    logger.LogError("Unsupported job: {Job}", command.Job);
                    return 2;
            }

            logger.LogInformation("Worker job '{Job}' completed successfully.", command.Job);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Worker job '{Job}' failed.", command.Job);
            return 1;
        }
    }

    private static int GetCurrentSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 7 ? now.Year : now.Year - 1;
    }
}
