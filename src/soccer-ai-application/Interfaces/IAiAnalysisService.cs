using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.Application.Interfaces;

public interface IAiAnalysisService
{
    bool SupportsDecisionExplanations => false;
    string? OpinionPromptHash => null;
    Task<AiDecisionExplanation?> ExplainDecisionAsync(DecisionExplanationInput input, CancellationToken ct = default) =>
        Task.FromResult<AiDecisionExplanation?>(null);
    Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items, CancellationToken cancellationToken = default);
    Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates, string? userMessage = null);
    Task<ChatCombinationIntent?> ParseChatIntentAsync(string query);
}
