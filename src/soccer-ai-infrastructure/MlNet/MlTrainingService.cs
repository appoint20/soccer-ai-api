using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.AutoML;
using Microsoft.ML.Data;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Evaluation;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>
/// Trains one binary model per market on fixture-market rows using a strict
/// TEMPORAL split: train &lt; cutoff, test ≥ cutoff (no random splitting — that
/// leaks future information into training).
///
/// IMPORTANT: the backtest window must never overlap training data. Pass a
/// cutoff that lies before the backtest start date; the cutoff used is
/// persisted in the evaluation report for auditing.
/// </summary>
public class MlTrainingService(
    ILogger<MlTrainingService> logger,
    MlTrainingDataBuilder dataBuilder,
    IServiceScopeFactory serviceScopeFactory) : IMlTrainingService
{
    private readonly MLContext _mlContext = new(seed: 42);

    /// <summary>Default temporal cutoff: 80% of rows (by time) train, newest 20% test.</summary>
    private const double DefaultTrainFraction = 0.80;
    private const uint ExperimentSecondsPerMarket = 30;

    public Task TrainModelsAsync(CancellationToken ct = default) => TrainModelsAsync(null, ct);

    public async Task TrainModelsAsync(DateTimeOffset? temporalCutoff, CancellationToken ct = default)
    {
        logger.LogInformation("Starting ML.NET training pipeline (temporal split)...");

        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var dixonColes = scope.ServiceProvider.GetRequiredService<IDixonColesModel>();
        var volatility = scope.ServiceProvider.GetRequiredService<ILeagueVolatilityService>();

        var fixtures = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.Status == "FT")
            .OrderBy(f => f.Date)
            .ToListAsync(ct);

        logger.LogInformation("Loaded {Count} finished fixtures", fixtures.Count);
        if (fixtures.Count == 0) return;

        var rows = await dataBuilder.BuildAsync(fixtures, dixonColes, volatility, ct);
        if (rows.Count == 0)
        {
            logger.LogWarning("No training rows produced — aborting");
            return;
        }

        // ── TEMPORAL split ──
        var cutoff = (temporalCutoff ?? DefaultCutoff(rows)).UtcDateTime;
        var trainRows = rows.Where(r => r.Date < cutoff).ToList();
        var testRows = rows.Where(r => r.Date >= cutoff).ToList();

        logger.LogInformation(
            "Temporal split at {Cutoff:yyyy-MM-dd}: {Train} train rows, {Test} test rows",
            cutoff, trainRows.Count, testRows.Count);

        if (trainRows.Count == 0 || testRows.Count == 0)
        {
            logger.LogWarning("Degenerate temporal split (train={Train}, test={Test}) — aborting",
                trainRows.Count, testRows.Count);
            return;
        }

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "models");
        Directory.CreateDirectory(outputDir);

        // ── Train + evaluate one binary model per market ──
        var allSamples = new List<PredictionSample>();
        foreach (var market in MarketTrainingRow.Markets.All)
        {
            ct.ThrowIfCancellationRequested();
            var samples = TrainAndEvaluateMarket(
                market,
                trainRows.Where(r => r.Market == market).ToList(),
                testRows.Where(r => r.Market == market).ToList(),
                outputDir);
            allSamples.AddRange(samples);
        }

        // ── Evaluation harness: Brier / log loss / calibration / accuracy ──
        var slices = EvaluationHarness.Evaluate(allSamples);
        foreach (var s in slices.Where(s => s.League == "ALL"))
        {
            logger.LogInformation(
                "[{Market}] n={N} Brier={Brier:F4} LogLoss={LogLoss:F4} Accuracy={Acc:P1}",
                s.Market, s.Samples, s.BrierScore, s.LogLoss, s.Accuracy);
        }

        var report = new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TemporalCutoffUtc = cutoff,
            TrainRows = trainRows.Count,
            TestRows = testRows.Count,
            Note = "Backtest window must start at or after TemporalCutoffUtc to avoid overlap with training data.",
            Slices = slices
        };

        var reportPath = Path.Combine(outputDir, "evaluation_report.json");
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        logger.LogInformation("Evaluation report written to {Path}", reportPath);
    }

    private static DateTimeOffset DefaultCutoff(List<MarketTrainingRow> rows)
    {
        var dates = rows.Select(r => r.Date).OrderBy(d => d).ToList();
        var index = Math.Clamp((int)(dates.Count * DefaultTrainFraction), 0, dates.Count - 1);
        return new DateTimeOffset(dates[index], TimeSpan.Zero);
    }

    private List<PredictionSample> TrainAndEvaluateMarket(
        string market,
        List<MarketTrainingRow> train,
        List<MarketTrainingRow> test,
        string outputDir)
    {
        try
        {
            if (train.Count < 100 || test.Count < 20)
            {
                logger.LogWarning("[{Market}] Not enough rows (train={Train}, test={Test}) — skipped",
                    market, train.Count, test.Count);
                return [];
            }

            logger.LogInformation("[{Market}] AutoML sweep ({Seconds}s) on {Train} rows...",
                market, ExperimentSecondsPerMarket, train.Count);

            var trainView = _mlContext.Data.LoadFromEnumerable(train);
            var testView = _mlContext.Data.LoadFromEnumerable(test);

            var columnInfo = new ColumnInformation { LabelColumnName = nameof(MarketTrainingRow.Label) };
            foreach (var meta in MarketTrainingRow.MetadataColumns)
                columnInfo.IgnoredColumnNames.Add(meta);

            var experiment = _mlContext.Auto().CreateBinaryClassificationExperiment(
                new BinaryExperimentSettings
                {
                    MaxExperimentTimeInSeconds = ExperimentSecondsPerMarket,
                    OptimizingMetric = BinaryClassificationMetric.AreaUnderRocCurve
                });

            var result = experiment.Execute(trainView, columnInfo);
            logger.LogInformation("[{Market}] Best trainer: {Trainer} (val AUC {Auc:F3})",
                market, result.BestRun.TrainerName, result.BestRun.ValidationMetrics.AreaUnderRocCurve);

            _mlContext.Model.Save(result.BestRun.Model, trainView.Schema,
                Path.Combine(outputDir, $"{market}_mlnet.zip"));

            // Score held-out rows for the evaluation harness.
            var scored = result.BestRun.Model.Transform(testView);
            return ExtractSamples(scored, test);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Market}] Training failed — market skipped", market);
            return [];
        }
    }

    private List<PredictionSample> ExtractSamples(IDataView scored, List<MarketTrainingRow> testRows)
    {
        var hasProbability = scored.Schema.Any(c => c.Name == "Probability" && !c.IsHidden);

        var probabilities = hasProbability
            ? scored.GetColumn<float>("Probability").ToList()
            : scored.GetColumn<float>("Score").Select(Sigmoid).ToList();

        if (!hasProbability)
            logger.LogWarning("Model has no calibrated Probability column — applying sigmoid to raw Score");

        var samples = new List<PredictionSample>(testRows.Count);
        for (var i = 0; i < testRows.Count && i < probabilities.Count; i++)
        {
            samples.Add(new PredictionSample(
                testRows[i].Market,
                (int)testRows[i].LeagueId,
                Math.Clamp(probabilities[i], 0f, 1f),
                testRows[i].Label));
        }

        return samples;
    }

    private static float Sigmoid(float score) => 1f / (1f + MathF.Exp(-score));
}
