using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Application.Services.Forecasts;

/// <summary>
/// Drives the forecast head-to-head during sync: settle what has finished, then
/// forecast what is coming.
/// </summary>
public interface IModelForecastSyncService
{
    /// <summary>Returns (settled, forecast) counts.</summary>
    Task<(int Settled, int Forecast)> RunAsync(int maxDaysAhead, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ModelForecastSyncService(
    IApplicationDbContext dbContext,
    IMatchForecastService forecastService,
    IModelForecastLedger ledger,
    ILogger<ModelForecastSyncService> logger) : IModelForecastSyncService
{
    public async Task<(int Settled, int Forecast)> RunAsync(
        int maxDaysAhead, CancellationToken cancellationToken = default)
    {
        // Settling is independent of the gateway: results already in the
        // database can be scored even when forecasting is switched off.
        var settled = await ledger.SettleAsync(cancellationToken);

        if (!forecastService.IsEnabled)
        {
            logger.LogInformation(
                "[Forecast] Skipping new forecasts — service disabled. Settled {Settled}.", settled);
            return (settled, 0);
        }

        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddDays(maxDaysAhead);

        // Only fixtures that already have a computed snapshot: the forecast is
        // built from the pipeline's own numbers, so without one there is nothing
        // to show a model and nothing to compare it against.
        var candidates = await (
            from fixture in dbContext.Fixtures
            join analysis in dbContext.FixtureAnalyses
                on fixture.Id equals analysis.FixtureId
            where fixture.Date > now
                  && fixture.Date <= horizon
                  && fixture.Status == "NS"
                  && analysis.Lang == "en"
                  && analysis.SnapshotJson != null
            select new { fixture.Id, analysis.SnapshotJson })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            logger.LogInformation(
                "[Forecast] No upcoming fixtures with snapshots within {Days} day(s). Settled {Settled}.",
                maxDaysAhead, settled);
            return (settled, 0);
        }

        // Skip fixtures every configured model has already forecast, so a second
        // run of the day costs nothing.
        var modelCount = forecastService.Models.Count;
        var fixtureIds = candidates.Select(c => c.Id).ToList();

        var alreadyDone = await dbContext.ModelForecasts
            .Where(f => fixtureIds.Contains(f.FixtureId))
            .GroupBy(f => f.FixtureId)
            .Where(g => g.Count() >= modelCount)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        var pending = candidates.Where(c => !alreadyDone.Contains(c.Id)).ToList();

        logger.LogInformation(
            "[Forecast] {Pending} fixture(s) to forecast across {Models} model(s) "
            + "({Skipped} already complete). Settled {Settled}.",
            pending.Count, modelCount, alreadyDone.Count, settled);

        var forecast = 0;
        foreach (var candidate in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var analysis = AnalysisSnapshotSerializer.Deserialize(candidate.SnapshotJson);
            if (analysis is null) continue;

            var forecasts = await forecastService.ForecastAsync(analysis, cancellationToken);
            if (forecasts.Count == 0) continue;

            await ledger.RecordAsync(analysis, forecasts, cancellationToken);
            forecast += forecasts.Count;
        }

        logger.LogInformation(
            "[Forecast] Recorded {Count} forecast(s) across {Models} model(s)", forecast, modelCount);

        return (settled, forecast);
    }
}
