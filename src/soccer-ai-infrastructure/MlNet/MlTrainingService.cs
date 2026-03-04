using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.AutoML;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.MlNet.Models;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Infrastructure.Services;

namespace SoccerAi.Infrastructure.MlNet;

public class MlTrainingService(ILogger<MlTrainingService> logger, MlTrainingDataBuilder dataBuilder, IServiceScopeFactory serviceScopeFactory) : IMlTrainingService
{
    // Fix random seed to ensure reproducible evaluations matching Python pipeline 
    private readonly MLContext _mlContext = new(seed: 42);

    public async Task TrainModelsAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting native C# ML.NET Training Pipeline (with AutoML tuning)...");

        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        
        var fixtures = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.Status == "FT")
            .OrderBy(f => f.Date)
            .ToListAsync(ct);
            
        logger.LogInformation("Loaded {Count} finished fixtures from DB for AutoML", fixtures.Count);
        if (fixtures.Count == 0) return;

        var mlData = await dataBuilder.BuildTrainingDataAsync(fixtures, ct);
        if (mlData.Count == 0) return;

        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "models");
        Directory.CreateDirectory(outputDir);
        logger.LogInformation("Models will be saved to: {OutputDir}", outputDir);

        // Binary Classification (AutoML Tuned)
        TrainBinaryModelTuned(split, "TargetOver25", "target_over25_mlnet.zip", outputDir);
        TrainBinaryModelTuned(split, "TargetBtts", "target_btts_mlnet.zip", outputDir);
        TrainBinaryModelTuned(split, "TargetGoals23", "target_goals23_mlnet.zip", outputDir);

        // Multiclass Classification (AutoML Tuned)
        TrainMulticlassModelTuned(split, "TargetResult", "target_result_mlnet.zip", outputDir);

        logger.LogInformation("All ML.NET models trained successfully.");
    }

    private void TrainBinaryModelTuned(DataOperationsCatalog.TrainTestData split, string labelColumn, string filename, string outputDir)
    {
        logger.LogInformation("Starting AutoML sweep for {Label} (15 seconds)...", labelColumn);

        var experimentSettings = new BinaryExperimentSettings
        {
            MaxExperimentTimeInSeconds = 15,
            OptimizingMetric = BinaryClassificationMetric.AreaUnderRocCurve
        };

        var experiment = _mlContext.Auto().CreateBinaryClassificationExperiment(experimentSettings);
        var result = experiment.Execute(split.TrainSet, labelColumnName: labelColumn);
        
        logger.LogInformation("--- {Label} Best AutoML Model ---", labelColumn);
        logger.LogInformation("Algorithm: {Algo}", result.BestRun.TrainerName);
        logger.LogInformation("Val ROC-AUC: {Val:P2}", result.BestRun.ValidationMetrics.AreaUnderRocCurve);

        // Evaluate on Unseen Test Data
        var predictions = result.BestRun.Model.Transform(split.TestSet);
        
        // Use EvaluateNonCalibrated because AutoML may choose an algorithm (like FastForest/Lbfgs)
        // that produces a 'Score' but no calibrated 'Probability' curve.
        var metrics = _mlContext.BinaryClassification.EvaluateNonCalibrated(predictions, labelColumnName: labelColumn);

        logger.LogInformation("--- {Label} Final Unseen Test Metrics ---", labelColumn);
        logger.LogInformation("Test Accuracy: {Accuracy:P2}", metrics.Accuracy);
        logger.LogInformation("Test F1 Score: {F1:P2}", metrics.F1Score);
        
        var filepath = Path.Combine(outputDir, filename);
        _mlContext.Model.Save(result.BestRun.Model, split.TrainSet.Schema, filepath);
    }

    private void TrainMulticlassModelTuned(DataOperationsCatalog.TrainTestData split, string labelColumn, string filename, string outputDir)
    {
        logger.LogInformation("Starting AutoML sweep for {Label} Multiclass (15 seconds)...", labelColumn);


        var experimentSettings = new MulticlassExperimentSettings
        {
            MaxExperimentTimeInSeconds = 15,
            OptimizingMetric = MulticlassClassificationMetric.MacroAccuracy
        };

        var experiment = _mlContext.Auto().CreateMulticlassClassificationExperiment(experimentSettings);
        var result = experiment.Execute(split.TrainSet, labelColumnName: labelColumn);

        logger.LogInformation("--- {Label} Best AutoML Model ---", labelColumn);
        logger.LogInformation("Algorithm: {Algo}", result.BestRun.TrainerName);
        logger.LogInformation("Val MacroAccuracy: {Val:P2}", result.BestRun.ValidationMetrics.MacroAccuracy);

        // Evaluate on unseen
        var predictions = result.BestRun.Model.Transform(split.TestSet);
        var metrics = _mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: labelColumn);

        logger.LogInformation("--- {Label} Metrics (Multiclass) ---", labelColumn);
        logger.LogInformation("Micro Accuracy: {Accuracy:P2}", metrics.MicroAccuracy);
        logger.LogInformation("Macro Accuracy: {Macro:P2}", metrics.MacroAccuracy);
        logger.LogInformation("Log Loss:       {Loss}", metrics.LogLoss);

        var filepath = Path.Combine(outputDir, filename);
        _mlContext.Model.Save(result.BestRun.Model, split.TrainSet.Schema, filepath);
    }
}
