using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.Application.Interfaces;

public interface IAiAnalysisService
{
    Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items);
    Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates);
    Task<ChatCombinationIntent?> ParseChatIntentAsync(string query);
}
