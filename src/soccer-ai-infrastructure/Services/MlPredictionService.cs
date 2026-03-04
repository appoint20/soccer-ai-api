using Microsoft.Extensions.Logging;
using Microsoft.ML;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Service that uses trained LightGBM ML.NET models to predict match outcomes natively.
/// </summary>
public class MlPredictionService : IMlPredictionService
{
    private readonly ILogger<MlPredictionService> _logger;
    private readonly MLContext _mlContext = new();

    private readonly PredictionEngine<MatchTrainingData, BinaryPrediction>? _over25Engine;
    private readonly PredictionEngine<MatchTrainingData, BinaryPrediction>? _bttsEngine;
    private readonly PredictionEngine<MatchTrainingData, BinaryPrediction>? _goals23Engine;
    private readonly PredictionEngine<MatchTrainingData, MulticlassPrediction>? _hdaEngine;

    private readonly string[] _featureColumns;
    private readonly bool _modelsLoaded;

    public MlPredictionService(ILogger<MlPredictionService> logger)
    {
        _logger = logger;
        
        var modelsDir = FindModelsDirectory();
        
        try
        {
            if (modelsDir != null && Directory.Exists(modelsDir))
            {
                var over25Path = Path.Combine(modelsDir, "target_over25_mlnet.zip");
                var bttsPath = Path.Combine(modelsDir, "target_btts_mlnet.zip");
                var goals23Path = Path.Combine(modelsDir, "target_goals23_mlnet.zip");
                var hdaPath = Path.Combine(modelsDir, "target_result_mlnet.zip");
                
                try {
                    if (File.Exists(over25Path))
                        _over25Engine = _mlContext.Model.CreatePredictionEngine<MatchTrainingData, BinaryPrediction>(
                            _mlContext.Model.Load(over25Path, out _));
                } catch (Exception ex) { _logger.LogError(ex, "Failed to load over25_model.zip"); }

                try {
                    if (File.Exists(bttsPath))
                        _bttsEngine = _mlContext.Model.CreatePredictionEngine<MatchTrainingData, BinaryPrediction>(
                            _mlContext.Model.Load(bttsPath, out _));
                } catch (Exception ex) { _logger.LogError(ex, "Failed to load btts_model.zip"); }

                try {
                    if (File.Exists(goals23Path))
                        _goals23Engine = _mlContext.Model.CreatePredictionEngine<MatchTrainingData, BinaryPrediction>(
                            _mlContext.Model.Load(goals23Path, out _));
                } catch (Exception ex) { _logger.LogError(ex, "Failed to load goals_2_3_model.zip"); }

                try {
                    if (File.Exists(hdaPath))
                        _hdaEngine = _mlContext.Model.CreatePredictionEngine<MatchTrainingData, MulticlassPrediction>(
                            _mlContext.Model.Load(hdaPath, out _));
                } catch (Exception ex) { _logger.LogError(ex, "Failed to load hda_model.zip"); }
                
                _modelsLoaded = _over25Engine != null && _bttsEngine != null && _hdaEngine != null;
                
                if (_modelsLoaded)
                    _logger.LogInformation("ML.NET models loaded successfully from {Path}", modelsDir);
                else
                    _logger.LogWarning("Some ML.NET models failed to load in {Path}", modelsDir);
            }
            else
            {
                _logger.LogWarning("ML models directory not found: {Path}", modelsDir);
                _modelsLoaded = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ML models");
            _modelsLoaded = false;
        }
        
        // This array serves documentational purposes and lengths validation.
        _featureColumns = new[]
        {
            "home_goals_scored_avg", "home_goals_conceded_avg", "home_xg_avg",
            "home_shots_avg", "home_shots_on_target_avg", "home_btts_rate",
            "home_over25_rate", "home_clean_sheet_rate", "home_failed_to_score_rate",
            "home_overall_goals_scored_avg", "home_overall_goals_conceded_avg",
            "home_overall_xg_avg", "home_overall_btts_rate", "home_overall_over25_rate",
            "home_overall_scored_diff", "home_overall_xg_diff",
            "home_overall_under_streak", "home_overall_over_streak", "home_overall_btts_streak",

            "away_goals_scored_avg", "away_goals_conceded_avg", "away_xg_avg",
            "away_shots_avg", "away_shots_on_target_avg", "away_btts_rate",
            "away_over25_rate", "away_clean_sheet_rate", "away_failed_to_score_rate",
            "away_overall_goals_scored_avg", "away_overall_goals_conceded_avg",
            "away_overall_xg_avg", "away_overall_btts_rate", "away_overall_over25_rate",
            "away_overall_scored_diff", "away_overall_xg_diff",
            "away_overall_under_streak", "away_overall_over_streak", "away_overall_btts_streak",
            "h2h_total_goals_avg", "h2h_btts_rate", "h2h_over25_rate",
            "league_avg_goals", "league_btts_rate", "league_over25_rate",
            "is_derby",
            "is_weekend", "day_of_week", "month", "season_month_idx",
            
            "home_elo", "away_elo",
            "home_rest_days", "away_rest_days", "rest_diff",
            "temp", "humidity", "is_artificial_turf",

            "home_win_implied_prob", "draw_implied_prob", "away_win_implied_prob",
            "over25_implied_prob", "btts_implied_prob"
        };
    }

    public async Task<FixturePrediction?> PredictAsync(int fixtureId, CancellationToken ct = default)
    {
        if (!_modelsLoaded)
        {
            _logger.LogWarning("ML models not loaded, cannot predict");
            return null;
        }
        
        _logger.LogInformation("Prediction requested for fixture {FixtureId}", fixtureId);
        return await Task.FromResult<FixturePrediction?>(null);
    }

    public async Task<Dictionary<string, double[]>> PredictFromFeaturesAsync(float[] features, CancellationToken ct = default)
    {
        var results = new Dictionary<string, double[]>();
        
        if (!_modelsLoaded || features.Length != _featureColumns.Length)
        {
            _logger.LogWarning("Cannot predict: models not loaded or feature count mismatch");
            return results;
        }
        
        var input = new MatchTrainingData { Features = features };

        // Output matching Python: [probFalse, probTrue]
        if (_over25Engine != null)
        {
            var p = _over25Engine.Predict(input);
            results["over25"] = new[] { 1.0 - p.Probability, (double)p.Probability };
        }
        
        if (_bttsEngine != null)
        {
            var p = _bttsEngine.Predict(input);
            results["btts"] = new[] { 1.0 - p.Probability, (double)p.Probability };
        }
        
        if (_goals23Engine != null)
        {
            var p = _goals23Engine.Predict(input);
            results["goals_2_3"] = new[] { 1.0 - p.Probability, (double)p.Probability };
        }
        
        if (_hdaEngine != null)
        {
            var p = _hdaEngine.Predict(input);
            // p.Score holds probabilities for Home (0), Draw (1), Away (2)
            results["hda"] = p.Score.Select(x => (double)x).ToArray();
        }
        
        return await Task.FromResult(results);
    }

    private string? FindModelsDirectory()
    {
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "models");
        if (Directory.Exists(baseDir)) return baseDir;

        baseDir = Path.Combine(AppContext.BaseDirectory, "data", "models");
        if (Directory.Exists(baseDir)) return baseDir;

        return null;
    }
}
