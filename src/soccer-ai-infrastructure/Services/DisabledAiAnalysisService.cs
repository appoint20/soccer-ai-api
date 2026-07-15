using Microsoft.Extensions.Logging;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// No-op IAiAnalysisService used when the LLM is disabled or no API key is
/// configured. The statistical flow (model, calibration, decisions, backtest)
/// works fully without the LLM — it only ever adds narrative text.
/// </summary>
public sealed class DisabledAiAnalysisService(
    ILogger<DisabledAiAnalysisService> logger) : IAiAnalysisService
{
    public Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items)
    {
        logger.LogWarning("AI analysis requested but the AI service is disabled (no API key) — skipping {Count} items", items.Count);
        return Task.FromResult(new Dictionary<int, AiBilingualResult>());
    }

    public Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates, string? userMessage = null)
    {
        logger.LogWarning("AI combination building requested but the AI service is disabled — returning empty list");
        return Task.FromResult(new List<CombinationDto>());
    }

    public Task<ChatCombinationIntent?> ParseChatIntentAsync(string query)
    {
        logger.LogWarning("AI chat intent parsing requested but the AI service is disabled — returning null");
        return Task.FromResult<ChatCombinationIntent?>(null);
    }
}
