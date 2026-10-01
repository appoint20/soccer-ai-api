using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;

namespace SoccerAi.Worker;

public sealed class CombinedPredictionWorker(IServiceScopeFactory scopes,
    IOptions<CombinedPredictionAutomationOptions> options, ILogger<CombinedPredictionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Scheduled combined predictions are disabled.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var report = await scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomation>().RunAsync(stoppingToken);
                logger.LogInformation("Combined predictions: {Status}; attempted {Attempted}, completed {Completed}, partial {Partial}, failed {Failed}. {Reason}",
                    report.Status, report.Attempted, report.Completed, report.Partial, report.Failed, report.Reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Scheduled combined predictions failed; waiting for the next tick."); }
            try { await Task.Delay(TimeSpan.FromMinutes(options.Value.PollIntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
