using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Forecasts;

/// <summary>
/// Records language-model forecasts alongside the pipeline's own, and settles
/// them once results arrive. This is the evidence base for "which forecaster is
/// actually better", so it is append-and-settle only — a recorded forecast is
/// never rewritten with hindsight.
/// </summary>
public interface IModelForecastLedger
{
    /// <summary>Records (or refreshes, pre-kickoff) each model's forecast for a fixture.</summary>
    Task RecordAsync(
        MatchAnalysis analysis,
        IReadOnlyList<GoalsForecast> forecasts,
        CancellationToken cancellationToken = default);

    /// <summary>Fills in actual scores for finished fixtures. Returns the number settled.</summary>
    Task<int> SettleAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ModelForecastLedger(
    IApplicationDbContext dbContext,
    ILogger<ModelForecastLedger> logger) : IModelForecastLedger
{
    public async Task RecordAsync(
        MatchAnalysis analysis,
        IReadOnlyList<GoalsForecast> forecasts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(forecasts);

        if (forecasts.Count == 0) return;

        var models = forecasts.Select(f => f.Model).ToList();

        var existing = await dbContext.ModelForecasts
            .Where(f => f.FixtureId == analysis.Id && models.Contains(f.Model))
            .ToDictionaryAsync(f => f.Model, cancellationToken);

        // The pipeline's view at this instant. Captured once and copied onto
        // every row so each record is a self-contained head-to-head.
        var p = analysis.Prediction;
        var systemOver25 = p?.Over25.Probability ?? 0;
        var systemBtts = p?.BTTS.Probability ?? 0;
        var systemExpectedGoals =
            analysis.HomeStats.AvgGoalsScoredLast3 + analysis.AwayStats.AvgGoalsScoredLast3;

        foreach (var forecast in forecasts)
        {
            if (existing.TryGetValue(forecast.Model, out var row))
            {
                // Refuse to overwrite a forecast the result has already judged.
                // Without this, a re-sync after kickoff would quietly replace a
                // wrong call with a better-informed one and inflate the score.
                if (row.IsSettled)
                {
                    logger.LogDebug(
                        "[Forecast] Skipping settled forecast for fixture {FixtureId} / {Model}",
                        analysis.Id, forecast.Model);
                    continue;
                }
            }
            else
            {
                row = new ModelForecast { FixtureId = analysis.Id, Model = forecast.Model };
                dbContext.ModelForecasts.Add(row);
            }

            row.PredictedAtUtc = DateTimeOffset.UtcNow;
            row.KickoffUtc = analysis.Date;

            row.ExpectedGoals = forecast.ExpectedGoals;
            row.PredictedHomeGoals = forecast.PredictedHomeGoals;
            row.PredictedAwayGoals = forecast.PredictedAwayGoals;
            row.Over25Probability = forecast.Over25Probability;
            row.BttsProbability = forecast.BttsProbability;
            row.Confidence = forecast.Confidence;
            row.Rationale = Truncate(forecast.Rationale, 1000);

            row.SystemExpectedGoals = systemExpectedGoals;
            row.SystemOver25Probability = systemOver25;
            row.SystemBttsProbability = systemBtts;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SettleAsync(CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.ModelForecasts
            .Where(f => f.SettledAtUtc == null)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return 0;

        var fixtureIds = pending.Select(f => f.FixtureId).Distinct().ToList();

        // "FT" only: a postponed or abandoned fixture has a score that means
        // nothing, and settling on it would score every model against noise.
        var finished = await dbContext.Fixtures
            .Where(f => fixtureIds.Contains(f.Id) && f.Status == "FT")
            .Select(f => new { f.Id, f.HomeGoal, f.AwayGoal })
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var settled = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var row in pending)
        {
            if (!finished.TryGetValue(row.FixtureId, out var result)) continue;

            row.ActualHomeGoals = result.HomeGoal;
            row.ActualAwayGoals = result.AwayGoal;
            row.SettledAtUtc = now;
            settled++;
        }

        if (settled > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("[Forecast] Settled {Count} model forecast(s)", settled);
        }

        return settled;
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];
}
