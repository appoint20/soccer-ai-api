using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>Chronological goal-rate training with untouched calibration blocks.</summary>
public sealed class GoalRateTrainingService(
    ILogger<GoalRateTrainingService> logger,
    GoalRateFeatureBuilder featureBuilder,
    IOptions<HybridModelOptions> options,
    IOptions<ConfluenceOptions> confluenceOptions,
    IServiceScopeFactory scopeFactory) : IGoalRateTrainingService
{
    private readonly HybridModelOptions _opt = options.Value;
    private readonly ConfluenceOptions _confluence = confluenceOptions.Value;
    private readonly MLContext _ml = new(seed: 42);

    public async Task TrainAsync(CancellationToken ct = default)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), _opt.ModelDirectory);

        if ((LastEvaluationAge(directory) ?? PublishedGenerationAge(directory)) is { } age &&
            age < TimeSpan.FromHours(_opt.RetrainIntervalHours))
        {
            logger.LogInformation(
                "[GoalRate] Last training evaluation is {Age:g} old, under the {Interval}h retrain "
                + "interval — skipping", age, _opt.RetrainIntervalHours);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var fixtures = await db.Fixtures.AsNoTracking()
            .Where(f => f.Status == "FT").OrderBy(f => f.Date).ToListAsync(ct);
        await TrainAndEvaluateAsync(fixtures, directory, publish: true, ct);
    }

    private static TimeSpan? LastEvaluationAge(string directory)
    {
        // A rejected model is still a completed training attempt. Otherwise a
        // failed gate retrains on every worker sync while no new data can help.
        try
        {
            var path = Path.Combine(directory, "goal_rate_evaluation.json");
            if (!File.Exists(path)) return null;
            var report = JsonSerializer.Deserialize<GoalRateEvaluation>(File.ReadAllText(path));
            if (report is null || report.FeatureSchema != GoalRateFeatureBuilder.SchemaVersion ||
                report.GeneratedAtUtc == default || report.GeneratedAtUtc > DateTimeOffset.UtcNow) return null;
            return DateTimeOffset.UtcNow - report.GeneratedAtUtc;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// How long ago the currently published generation was created, or null when
    /// nothing is published or the metadata cannot be read.
    /// </summary>
    /// <remarks>
    /// Null deliberately means "train": an unreadable or absent pointer is the
    /// state a fresh deployment is in, and refusing to train there would leave it
    /// permanently on the Dixon-Coles fallback.
    /// </remarks>
    private TimeSpan? PublishedGenerationAge(string directory)
    {
        try
        {
            var pointerPath = Path.Combine(directory, GoalRateArtifact.PointerFile);
            if (!File.Exists(pointerPath)) return null;

            var pointer = JsonSerializer.Deserialize<GoalRateGenerationPointer>(
                File.ReadAllText(pointerPath));
            if (pointer is null || !Guid.TryParseExact(pointer.Generation, "N", out _)) return null;

            var manifestPath = Path.Combine(
                directory, "goal-rate-generations", pointer.Generation, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            var manifest = JsonSerializer.Deserialize<GoalRateArtifact>(File.ReadAllText(manifestPath));
            if (manifest is null || manifest.CreatedAtUtc == default) return null;

            return DateTimeOffset.UtcNow - manifest.CreatedAtUtc;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GoalRate] Could not read the published generation's age — training");
            return null;
        }
    }

    /// <summary>Offline audit entry point: no database writes or network calls.</summary>
    public async Task<GoalRateEvaluation?> TrainAndEvaluateAsync(
        IReadOnlyCollection<Fixture> fixtures, string directory, bool publish = false,
        CancellationToken ct = default)
    {
        if (_opt.ValidationFraction is <= 0 or >= 0.5 ||
            _opt.WalkForwardWarmupFraction is <= 0 or >= 0.9 || _opt.WalkForwardFolds < 1)
            throw new InvalidOperationException("Invalid chronological training fractions/folds");
        var rows = featureBuilder.Build(fixtures).Where(r => r.IsFinished).ToList();
        if (rows.Count < _opt.MinTrainingRows)
        {
            logger.LogWarning("[GoalRate] {Rows} rows below training minimum {Minimum}", rows.Count, _opt.MinTrainingRows);
            return null;
        }
        Directory.CreateDirectory(directory);
        var report = EvaluateWalkForward(rows, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "goal_rate_evaluation.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        if (!publish || !report.PublicationGatePassed) return report;

        // The calibration tail must remain excluded from the shipped trees too.
        var (fit, calibrationRows) = SplitCalibration(rows, _opt.ValidationFraction);
        var fitView = _ml.Data.LoadFromEnumerable(fit);
        var home = TrainOne(fitView, nameof(GoalRateRow.GoalsHome));
        var away = TrainOne(fitView, nameof(GoalRateRow.GoalsAway));
        var calibration = FitCalibration(home, away,
            _ml.Data.LoadFromEnumerable(calibrationRows), calibrationRows);
        var generation = Guid.NewGuid().ToString("N");
        var target = Path.Combine(directory, "goal-rate-generations", generation);
        Directory.CreateDirectory(target);
        _ml.Model.Save(home, fitView.Schema, Path.Combine(target, "home.zip"));
        _ml.Model.Save(away, fitView.Schema, Path.Combine(target, "away.zip"));
        await File.WriteAllTextAsync(Path.Combine(target, "calibration.json"), JsonSerializer.Serialize(calibration), ct);
        // Reload and score before publication; all files become visible through
        // one atomic pointer, never a home model from a different training run.
        VerifySavedModel(Path.Combine(target, "home.zip"), home, calibrationRows);
        VerifySavedModel(Path.Combine(target, "away.zip"), away, calibrationRows);
        var manifest = new GoalRateArtifact
        {
            Generation = generation, CreatedAtUtc = DateTimeOffset.UtcNow, Trainer = TrainerUsed,
            TrainingThroughUtc = fit[^1].Date, CalibrationFromUtc = calibrationRows[0].Date,
            CalibrationThroughUtc = calibrationRows[^1].Date, Features = GoalRateRow.FeatureColumns(),
            DixonColes = featureBuilder.DixonColesSettings, LambdaMin = _opt.LambdaMin, LambdaMax = _opt.LambdaMax,
            Sha256 = new[] { "home.zip", "away.zip", "calibration.json" }
                .ToDictionary(name => name, name => GoalRateArtifact.Hash(Path.Combine(target, name)))
        };
        await File.WriteAllTextAsync(Path.Combine(target, "manifest.json"), JsonSerializer.Serialize(manifest), ct);
        await File.WriteAllTextAsync(Path.Combine(target, "evaluation.json"), JsonSerializer.Serialize(report), ct);
        var pointer = Path.Combine(directory, GoalRateArtifact.PointerFile);
        var temporary = pointer + "." + generation + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new GoalRateGenerationPointer(generation)), ct);
        File.Move(temporary, pointer, overwrite: true);
        return report;
    }

    private void VerifySavedModel(string path, ITransformer expected, List<GoalRateRow> rows)
    {
        var view = _ml.Data.LoadFromEnumerable(rows.Take(32));
        var before = expected.Transform(view).GetColumn<float>("Score").ToArray();
        var after = _ml.Model.Load(path, out _).Transform(view).GetColumn<float>("Score").ToArray();
        if (before.Length != after.Length || before.Where((v, i) => !float.IsFinite(v) || Math.Abs(v - after[i]) > 1e-6).Any())
            throw new InvalidDataException("Saved model failed prediction parity");
    }

    public static (List<GoalRateRow> Fit, List<GoalRateRow> Calibration) SplitCalibration(
        IReadOnlyList<GoalRateRow> rows, double fraction)
    {
        var days = rows.Select(r => r.Date.Date).Distinct().Order().ToArray();
        if (days.Length < 2) throw new InvalidOperationException("Calibration requires separate UTC days");
        var cutoff = days[Math.Clamp((int)(days.Length * (1 - fraction)), 1, days.Length - 1)];
        return (rows.Where(r => r.Date.Date < cutoff).ToList(), rows.Where(r => r.Date.Date >= cutoff).ToList());
    }

    private GoalRateEvaluation EvaluateWalkForward(List<GoalRateRow> rows, CancellationToken ct)
    {
        var days = rows.Select(r => r.Date.Date).Distinct().Order().ToArray();
        var start = Math.Max(2, (int)(days.Length * _opt.WalkForwardWarmupFraction));
        var over = new List<(double P, bool Y)>();
        var btts = new List<(double P, bool Y)>();
        var priorOver = new List<(double P, bool Y)>();
        var priorBtts = new List<(double P, bool Y)>();
        var dcOver = new List<(double P, bool Y)>();
        var dcBtts = new List<(double P, bool Y)>();
        var predictedHome = new List<double>();
        var predictedAway = new List<double>();
        var actualHome = new List<double>();
        var actualAway = new List<double>();
        var folds = new List<GoalRateFold>();
        var dc = featureBuilder.DixonColesSettings;
        for (var fold = 0; fold < _opt.WalkForwardFolds; fold++)
        {
            ct.ThrowIfCancellationRequested();
            var lo = start + (days.Length - start) * fold / _opt.WalkForwardFolds;
            var hi = start + (days.Length - start) * (fold + 1) / _opt.WalkForwardFolds;
            if (lo >= days.Length || hi <= lo) continue;
            var history = rows.Where(r => r.Date.Date < days[lo]).ToList();
            var test = rows.Where(r => r.Date.Date >= days[lo] && (hi == days.Length || r.Date.Date < days[hi])).ToList();
            if (test.Count < 50) continue;
            var (fit, cal) = SplitCalibration(history, _opt.ValidationFraction);
            var fitView = _ml.Data.LoadFromEnumerable(fit);
            var home = TrainOne(fitView, nameof(GoalRateRow.GoalsHome));
            var away = TrainOne(fitView, nameof(GoalRateRow.GoalsAway));
            var correction = FitCalibration(home, away, _ml.Data.LoadFromEnumerable(cal), cal);
            var testView = _ml.Data.LoadFromEnumerable(test);
            var lh = home.Transform(testView).GetColumn<float>("Score").ToArray();
            var la = away.Transform(testView).GetColumn<float>("Score").ToArray();
            if (lh.Length != test.Count || la.Length != test.Count) throw new InvalidDataException("Prediction row mismatch");
            var pOver = history.Count(r => r.GoalsHome + r.GoalsAway > 2) / (double)history.Count;
            var pBtts = history.Count(r => r.GoalsHome > 0 && r.GoalsAway > 0) / (double)history.Count;
            for (var i = 0; i < test.Count; i++)
            {
                if (!float.IsFinite(lh[i]) || !float.IsFinite(la[i])) throw new InvalidDataException("Non-finite held-out rate");
                var h = Math.Clamp(lh[i] * correction.HomeScale, _opt.LambdaMin, _opt.LambdaMax);
                var a = Math.Clamp(la[i] * correction.AwayScale, _opt.LambdaMin, _opt.LambdaMax);
                var m = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(h, a, dc.Rho, dc.MaxGoals));
                var yOver = test[i].GoalsHome + test[i].GoalsAway > 2;
                var yBtts = test[i].GoalsHome > 0 && test[i].GoalsAway > 0;
                over.Add((m.Over25, yOver)); btts.Add((m.Btts, yBtts));
                priorOver.Add((pOver, yOver)); priorBtts.Add((pBtts, yBtts));
                dcOver.Add((test[i].DcLambdaSum > 0 ? test[i].DcOver25 : pOver, yOver));
                dcBtts.Add((test[i].DcLambdaSum > 0 ? test[i].DcBtts : pBtts, yBtts));
                predictedHome.Add(h); predictedAway.Add(a);
                actualHome.Add(test[i].GoalsHome); actualAway.Add(test[i].GoalsAway);
            }
            folds.Add(new GoalRateFold(fit.Count, cal.Count, test.Count, fit[^1].Date, cal[0].Date, cal[^1].Date,
                test[0].Date, test[^1].Date, correction.HomeScale, correction.AwayScale));
            logger.LogInformation("[GoalRate] Fold {Fold}: fit {Fit}; unseen calibration {Cal}; test {Test}", fold + 1, fit.Count, cal.Count, test.Count);
        }
        var overThreshold = PickSelector.ConfidenceFloorFor(ConfluenceRuleEngine.Markets.Over25, _confluence);
        var bttsThreshold = PickSelector.ConfidenceFloorFor(ConfluenceRuleEngine.Markets.Btts, _confluence);
        var overMetrics = Summarise(over, overThreshold);
        var bttsMetrics = Summarise(btts, bttsThreshold);
        var priorOverMetrics = Summarise(priorOver, overThreshold);
        var priorBttsMetrics = Summarise(priorBtts, bttsThreshold);
        var dcOverMetrics = Summarise(dcOver, overThreshold);
        var dcBttsMetrics = Summarise(dcBtts, bttsThreshold);
        return new GoalRateEvaluation
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow, TrainRows = rows.Count, EvaluatedRows = over.Count,
            HeldOutFrom = folds.Count > 0 ? folds[0].TestFromUtc : default, Trainer = TrainerUsed, Folds = folds,
            LambdaHomeMean = predictedHome.Count > 0 ? predictedHome.Average() : 0,
            LambdaAwayMean = predictedAway.Count > 0 ? predictedAway.Average() : 0,
            ActualHomeGoalsMean = actualHome.Count > 0 ? actualHome.Average() : 0,
            ActualAwayGoalsMean = actualAway.Count > 0 ? actualAway.Average() : 0,
            Over25 = overMetrics, Btts = bttsMetrics,
            PriorOver25 = priorOverMetrics, PriorBtts = priorBttsMetrics,
            DixonColesOver25 = dcOverMetrics, DixonColesBtts = dcBttsMetrics,
            Over25Thresholds = Sweep(over), BttsThresholds = Sweep(btts),
            PublicationGatePassed = over.Count >= 500 &&
                overMetrics.BrierScore <= priorOverMetrics.BrierScore && bttsMetrics.BrierScore <= priorBttsMetrics.BrierScore &&
                overMetrics.LogLoss <= priorOverMetrics.LogLoss && bttsMetrics.LogLoss <= priorBttsMetrics.LogLoss &&
                overMetrics.BrierScore <= dcOverMetrics.BrierScore && bttsMetrics.BrierScore <= dcBttsMetrics.BrierScore &&
                overMetrics.LogLoss <= dcOverMetrics.LogLoss && bttsMetrics.LogLoss <= dcBttsMetrics.LogLoss
        };
    }

    /// <summary>Which trainer actually produced the models on the last run.</summary>
    public string TrainerUsed { get; private set; } = "";

    private ITransformer TrainOne(IDataView trainView, string labelColumn)
    {
        var features = GoalRateRow.FeatureColumns();
        logger.LogInformation("[GoalRate] Training {Label} on {N} features...", labelColumn, features.Length);

        var prefix = _ml.Transforms
            .Concatenate("Features", features)
            .Append(_ml.Transforms.ReplaceMissingValues(
                "Features", replacementMode: MissingValueReplacingEstimator.ReplacementMode.Mean));

        // Native LightGBM is unavailable on some platforms; record any fallback.
        try
        {
            var model = prefix.Append(_ml.Regression.Trainers.LightGbm(BuildLightGbmOptions(labelColumn)))
                .Fit(trainView);
            TrainerUsed = "LightGbm";
            return model;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException
                                       or EntryPointNotFoundException)
        {
            logger.LogWarning(
                "[GoalRate] Native LightGBM unavailable on this platform ({Message}). "
                + "Falling back to FastTreeTweedie — "
                + "models trained here are NOT equivalent to a production build",
                ex.Message.Split('\n')[0]);

            var model = prefix.Append(_ml.Regression.Trainers.FastTreeTweedie(
                    labelColumnName: labelColumn,
                    featureColumnName: "Features",
                    numberOfTrees: _opt.Trees,
                    numberOfLeaves: _opt.Leaves,
                    minimumExampleCountPerLeaf: _opt.MinExamplesPerLeaf,
                    learningRate: _opt.LearningRate))
                .Fit(trainView);
            TrainerUsed = "FastTreeTweedie";
            return model;
        }
    }

    // ML.NET exposes squared-error regression here; this is not a Poisson loss.
    private Microsoft.ML.Trainers.LightGbm.LightGbmRegressionTrainer.Options BuildLightGbmOptions(
        string labelColumn) => new()
    {
        LabelColumnName = labelColumn,
        FeatureColumnName = "Features",
        NumberOfIterations = _opt.Trees,
        LearningRate = _opt.LearningRate,
        NumberOfLeaves = _opt.Leaves,
        MinimumExampleCountPerLeaf = _opt.MinExamplesPerLeaf,
        UseCategoricalSplit = false,
        Booster = new Microsoft.ML.Trainers.LightGbm.GradientBooster.Options
        {
            L2Regularization = _opt.L2Regularization,
            FeatureFraction = _opt.FeatureFraction,
            SubsampleFraction = _opt.SubsampleFraction,
            SubsampleFrequency = 1
        }
    };

    /// <summary>
    /// Fits the one-parameter-per-side λ correction: the factor that makes mean
    /// predicted goals match mean scored goals on data the trees never saw.
    /// </summary>
    private GoalRateCalibration FitCalibration(
        ITransformer home, ITransformer away, IDataView view, List<GoalRateRow> rows)
    {
        var predHome = home.Transform(view).GetColumn<float>("Score").Take(rows.Count).ToArray();
        var predAway = away.Transform(view).GetColumn<float>("Score").Take(rows.Count).ToArray();

        static double Scale(IReadOnlyList<float> predicted, IEnumerable<float> actual)
        {
            if (predicted.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Non-finite calibration prediction");
            var p = predicted.Count > 0 ? predicted.Average(v => (double)v) : 0;
            var a = actual.Average(v => (double)v);
            // A degenerate predictor gets no correction rather than an infinite one.
            if (p <= 1e-6) return 1.0;
            // Bounded: this exists to remove a known few-percent shrinkage, not
            // to rescue a model that is wrong by a factor of two.
            return Math.Clamp(a / p, 0.5, 2.0);
        }

        return new GoalRateCalibration
        {
            HomeScale = Math.Round(Scale(predHome, rows.Select(r => r.GoalsHome)), 6),
            AwayScale = Math.Round(Scale(predAway, rows.Select(r => r.GoalsAway)), 6),
            FittedAtUtc = DateTimeOffset.UtcNow,
            ValidationRows = rows.Count
        };
    }

    private static readonly double[] SweepPoints =
        [0.50, 0.54, 0.56, 0.58, 0.60, 0.62, 0.64, 0.66, 0.68, 0.70, 0.75, 0.80, 0.85, 0.90];

    /// <summary>
    /// Hit rate and volume at each candidate publish threshold.
    /// </summary>
    /// <remarks>
    /// The threshold is the single lever that trades board size against hit
    /// rate, and the right setting moves as the model and the fixture list
    /// change. Emitting the whole curve each run means the setting can be
    /// re-derived from the current model rather than inherited from whatever
    /// was true when it was first chosen.
    /// </remarks>
    private static List<ThresholdPoint> Sweep(List<(double P, bool Y)> samples) =>
        [.. SweepPoints.Select(t =>
        {
            var published = samples.Where(s => s.P >= t).ToList();
            var hits = published.Count(s => s.Y);
            return new ThresholdPoint
            {
                Threshold = t,
                Published = published.Count,
                PublishedShare = samples.Count > 0
                    ? Math.Round(published.Count / (double)samples.Count * 100, 2) : 0,
                HitRate = published.Count > 0
                    ? Math.Round(hits / (double)published.Count * 100, 2) : 0,
                HitRateLowerBound = published.Count > 0
                    ? Math.Round(WilsonLowerBound(hits, published.Count) * 100, 2) : 0
            };
        })];

    private static MarketEvaluation Summarise(List<(double P, bool Y)> samples, double threshold)
    {
        if (samples.Count == 0) return new MarketEvaluation();

        var published = samples.Where(s => s.P >= threshold).ToList();
        var hits = published.Count(s => s.Y);

        return new MarketEvaluation
        {
            Samples = samples.Count,
            BaseRate = Math.Round(samples.Count(s => s.Y) / (double)samples.Count * 100, 2),
            LogLoss = Math.Round(samples.Average(s => -(s.Y ? Math.Log(Math.Clamp(s.P, 1e-6, 1 - 1e-6)) : Math.Log(1 - Math.Clamp(s.P, 1e-6, 1 - 1e-6)))), 6),
            BrierScore = Math.Round(samples.Average(s => Math.Pow(s.P - (s.Y ? 1 : 0), 2)), 4),
            AccuracyAtHalf = Math.Round(
                samples.Count(s => (s.P >= 0.5) == s.Y) / (double)samples.Count * 100, 2),
            PublishThreshold = threshold,
            Published = published.Count,
            PublishedShare = Math.Round(published.Count / (double)samples.Count * 100, 2),
            PublishedHitRate = published.Count > 0
                ? Math.Round(hits / (double)published.Count * 100, 2)
                : 0,
            PublishedHitRateLowerBound = published.Count > 0
                ? Math.Round(WilsonLowerBound(hits, published.Count) * 100, 2)
                : 0
        };
    }

    /// <summary>
    /// 95% Wilson lower bound. Reported alongside every hit rate because a
    /// flattering number on a small published set is the exact failure mode
    /// this project has already been burned by.
    /// </summary>
    private static double WilsonLowerBound(int hits, int n)
    {
        if (n == 0) return 0;
        const double z = 1.96;
        var p = hits / (double)n;
        var den = 1 + z * z / n;
        var centre = (p + z * z / (2.0 * n)) / den;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / den;
        return Math.Max(0, centre - half);
    }
}

public sealed record GoalRateEvaluation
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string EvaluationProtocol { get; init; } = "Chronological UTC-day folds; disjoint fit/calibration/test; thresholds descriptive only; no odds/EV qualification";
    public string FeatureSchema { get; init; } = GoalRateFeatureBuilder.SchemaVersion;
    public bool PublicationGatePassed { get; init; }
    public string PublicationGate { get; init; } = "At least 500 OOF forecasts and both market Brier/log-loss no worse than prefix frequency prior AND Dixon-Coles baseline; does not prove profitability or 80%";
    public IReadOnlyList<GoalRateFold> Folds { get; init; } = [];
    public MarketEvaluation PriorOver25 { get; init; } = new();
    public MarketEvaluation PriorBtts { get; init; } = new();
    public MarketEvaluation DixonColesOver25 { get; init; } = new();
    public MarketEvaluation DixonColesBtts { get; init; } = new();
    /// <summary>Labelled rows available in total.</summary>
    public int TrainRows { get; init; }

    /// <summary>Out-of-fold predictions the metrics below are computed on.</summary>
    public int EvaluatedRows { get; init; }
    public DateTime HeldOutFrom { get; init; }

    /// <summary>
    /// Which trainer produced these models. Platforms without a native LightGBM
    /// fall back to FastTreeTweedie, which scores worse — so a report claiming
    /// FastTreeTweedie did not come from a production-equivalent build.
    /// </summary>
    public string Trainer { get; init; } = "";

    /// <summary>
    /// Mean learned λ against the mean goals actually scored. These pairs should
    /// agree closely; a large gap means the regressor's output is on the wrong
    /// scale and every downstream probability is skewed.
    /// </summary>
    public double LambdaHomeMean { get; init; }
    public double LambdaAwayMean { get; init; }
    public double ActualHomeGoalsMean { get; init; }
    public double ActualAwayGoalsMean { get; init; }

    /// <summary>The λ corrections that were fitted and are shipped with the models.</summary>
    public double HomeScale { get; init; }
    public double AwayScale { get; init; }
    public MarketEvaluation Over25 { get; init; } = new();
    public MarketEvaluation Btts { get; init; } = new();

    /// <summary>Hit rate and volume at every candidate publish threshold.</summary>
    public List<ThresholdPoint> Over25Thresholds { get; init; } = [];
    public List<ThresholdPoint> BttsThresholds { get; init; } = [];
}

/// <summary>One point on the accuracy-versus-volume curve.</summary>
public sealed class ThresholdPoint
{
    public double Threshold { get; init; }
    public int Published { get; init; }
    public double PublishedShare { get; init; }
    public double HitRate { get; init; }
    public double HitRateLowerBound { get; init; }
}

public sealed class MarketEvaluation
{
    public int Samples { get; init; }
    public double BaseRate { get; init; }
    public double BrierScore { get; init; }
    public double LogLoss { get; init; }
    public double AccuracyAtHalf { get; init; }
    public double PublishThreshold { get; init; }
    public int Published { get; init; }
    public double PublishedShare { get; init; }
    public double PublishedHitRate { get; init; }
    public double PublishedHitRateLowerBound { get; init; }
}

/// <summary>
/// Multiplicative λ corrections that travel with the trained models.
/// Serving must apply these; scoring the raw model output is miscalibrated.
/// </summary>
public sealed class GoalRateCalibration
{
    public double HomeScale { get; init; } = 1.0;
    public double AwayScale { get; init; } = 1.0;
    public DateTimeOffset FittedAtUtc { get; init; }
    public int ValidationRows { get; init; }
}

public sealed record GoalRateFold(int FitRows, int CalibrationRows, int TestRows, DateTime FitThroughUtc,
    DateTime CalibrationFromUtc, DateTime CalibrationThroughUtc, DateTime TestFromUtc, DateTime TestThroughUtc,
    double HomeScale, double AwayScale);
