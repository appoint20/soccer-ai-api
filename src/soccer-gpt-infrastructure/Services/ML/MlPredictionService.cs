using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.ML;

public class MlPredictionService(
    SoccerGoalScoringModel model,
    FeatureEngineeringService featureService,
    ILogger<MlPredictionService> logger) : IMlPredictionService
{
    private bool _hasTrained = false;
    private readonly SemaphoreSlim _trainLock = new(1, 1);

    public async Task<MatchPredictionOutput?> PredictMatchAsync(UpcomingMatchDto match, List<HistoricalMatchDto> allHistory)
    {
        // 1. Check and Train if needed
        if (!model.HasModels && !_hasTrained)
        {
            await _trainLock.WaitAsync();
            try
            {
                if (!model.HasModels) // Double-check inside lock
                {
                    logger.LogInformation("ML Models missing. Triggering Training with {Count} historical matches...", allHistory.Count);
                    if (allHistory.Count > 100)
                    {
                        var trainingData = await featureService.CreateTrainingDatasetAsync(allHistory);
                        model.TrainAndSave(trainingData);
                        _hasTrained = true;
                        logger.LogInformation("Training Completed.");
                    }
                    else
                    {
                        logger.LogWarning("Not enough historical data to train ML model.");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to train ML models.");
                return null;
            }
            finally
            {
                _trainLock.Release();
            }
        }
        
        // 2. Prepare Input Features
        try
        {
            // Convert upcoming match to "target" historical match format
             if (!DateTime.TryParse($"{match.Date} {match.Time}", out var matchDate))
             {
                 matchDate = DateTime.UtcNow; // Fallback
             }

             var target = new HistoricalMatchDto
             {
                 Date = matchDate,
                 HomeTeam = match.HomeTeam,
                 AwayTeam = match.AwayTeam,
                 // Map Odds
                 Odds = match.Odds != null ? new MatchOddsDto 
                 { 
                     Over25 = match.Odds.Over25,
                     Draw = match.Odds.Draw,
                     HomeWin = match.Odds.HomeWin,
                     AwayWin = match.Odds.AwayWin
                     // Others optional for now
                 } : null
             };

             var features = await featureService.CalculateFeaturesAsync(target, allHistory);
             
             // 3. Predict
             var prediction = model.Predict(features);
             return prediction;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating prediction for {Home} vs {Away}", match.HomeTeam, match.AwayTeam);
            return null;
        }
    }
}
