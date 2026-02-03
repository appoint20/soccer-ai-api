using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Service that uses trained XGBoost/ONNX models to predict match outcomes.
/// </summary>
public class MlPredictionService : IMlPredictionService, IDisposable
{
    private readonly ILogger<MlPredictionService> _logger;
    
    private readonly InferenceSession? _over25Model;
    private readonly InferenceSession? _bttsModel;
    private readonly InferenceSession? _goals23Model;
    private readonly InferenceSession? _hdaModel;
    
    private readonly string[] _featureColumns;
    private readonly bool _modelsLoaded;

    public MlPredictionService(ILogger<MlPredictionService> logger)
    {
        _logger = logger;
        
        var modelsDir = Path.Combine(
            Directory.GetCurrentDirectory(), 
            "..", "..", "scripts", "ml", "models");
        
        try
        {
            if (Directory.Exists(modelsDir))
            {
                var over25Path = Path.Combine(modelsDir, "over25_model.onnx");
                var bttsPath = Path.Combine(modelsDir, "btts_model.onnx");
                var goals23Path = Path.Combine(modelsDir, "goals_2_3_model.onnx");
                var hdaPath = Path.Combine(modelsDir, "hda_model.onnx");
                
                if (File.Exists(over25Path))
                    _over25Model = new InferenceSession(over25Path);
                if (File.Exists(bttsPath))
                    _bttsModel = new InferenceSession(bttsPath);
                if (File.Exists(goals23Path))
                    _goals23Model = new InferenceSession(goals23Path);
                if (File.Exists(hdaPath))
                    _hdaModel = new InferenceSession(hdaPath);
                
                _modelsLoaded = _over25Model != null && _bttsModel != null;
                
                if (_modelsLoaded)
                    _logger.LogInformation("ML models loaded successfully from {Path}", modelsDir);
                else
                    _logger.LogWarning("Some ML models not found in {Path}", modelsDir);
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
        
        _featureColumns = new[]
        {
            "home_goals_scored_avg", "home_goals_conceded_avg", "home_xg_avg",
            "home_shots_avg", "home_shots_on_target_avg", "home_btts_rate",
            "home_over25_rate", "home_clean_sheet_rate", "home_failed_to_score_rate",
            "away_goals_scored_avg", "away_goals_conceded_avg", "away_xg_avg",
            "away_shots_avg", "away_shots_on_target_avg", "away_btts_rate",
            "away_over25_rate", "away_clean_sheet_rate", "away_failed_to_score_rate",
            "h2h_total_goals_avg", "h2h_btts_rate", "h2h_over25_rate",
            "league_avg_goals", "league_btts_rate", "league_over25_rate",
            "is_derby",
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
        
        // TODO: Implement feature extraction from fixture ID
        // For now, return null - this needs to query the fixture and build features
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
        
        // Create input tensor
        var inputTensor = new DenseTensor<float>(features, new[] { 1, features.Length });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", inputTensor) };
        
        // Run predictions
        // Helper to extract pros
        double[] ExtractProbs(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output)
        {
            try 
            {
                foreach (var item in output)
                {
                    _logger.LogInformation("Output: Name={Name}, Type={Type}", item.Name, item.Value?.GetType().Name);
                }

                // Try float tensor
                var floatTensor = output.Select(x => x.AsTensor<float>()).FirstOrDefault(x => x != null);
                if (floatTensor != null)
                {
                    _logger.LogInformation("Found Float Tensor with {Length} elements", floatTensor.Length);
                    return floatTensor.ToArray().Select(p => (double)p).ToArray();
                }

                // Try double tensor
                var doubleTensor = output.Select(x => x.AsTensor<double>()).FirstOrDefault(x => x != null);
                if (doubleTensor != null)
                {
                    _logger.LogInformation("Found Double Tensor with {Length} elements", doubleTensor.Length);
                    return doubleTensor.ToArray();
                }

                _logger.LogWarning("Could not find float or double tensor in output");
                return Array.Empty<double>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting probabilities");
                return Array.Empty<double>();
            }
        }

        if (_over25Model != null)
        {
            using var output = _over25Model.Run(inputs);
            results["over25"] = ExtractProbs(output);
        }
        
        if (_bttsModel != null)
        {
            using var output = _bttsModel.Run(inputs);
            results["btts"] = ExtractProbs(output);
        }
        
        if (_goals23Model != null)
        {
            using var output = _goals23Model.Run(inputs);
            results["goals_2_3"] = ExtractProbs(output);
        }
        
        if (_hdaModel != null)
        {
            using var output = _hdaModel.Run(inputs);
            results["hda"] = ExtractProbs(output);
        }
        
        return await Task.FromResult(results);
    }

    public void Dispose()
    {
        _over25Model?.Dispose();
        _bttsModel?.Dispose();
        _goals23Model?.Dispose();
        _hdaModel?.Dispose();
    }
}
