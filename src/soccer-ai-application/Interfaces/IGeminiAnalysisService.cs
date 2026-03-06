using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.Application.Interfaces;

public interface IGeminiAnalysisService
{
    Task<Dictionary<int, GeminiBilingualResult>> AnalyzeBatchAsync(List<GeminiBatchItem> items);
    Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates);
}
