using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Interfaces;

public interface IMlPredictionService
{
    Task<MatchPredictionOutput?> PredictMatchAsync(UpcomingMatchDto match, List<HistoricalMatchDto> allHistory);
    
    // Optional: Expose ability to force train?
    // Task TrainAsync(List<HistoricalMatchDto> trainingData);
}
