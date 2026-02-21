using soccer_gpt_application.Features.Combinations;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public class GeminiBatchItem
{
    public int FixtureId { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public TeamStats HomeStats { get; set; } = TeamStats.Empty;
    public TeamStats AwayStats { get; set; } = TeamStats.Empty;
    public WeightedPrediction? Prediction { get; set; }
}

public interface IGeminiAnalysisService
{
    Task<Dictionary<int, GeminiAnalysis>> AnalyzeBatchAsync(List<GeminiBatchItem> items);
    Task<List<CombinationDto>> BuildCombinationsAsync(List<CombinationMatchDto> candidates);
}
