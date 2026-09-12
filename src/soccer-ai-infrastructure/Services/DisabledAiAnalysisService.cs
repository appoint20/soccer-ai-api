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
    public Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0) return Task.FromResult(new Dictionary<int, AiBilingualResult>());
        throw new SoccerAi.Application.Exceptions.ExternalApiException("AI narratives",
            "AI narratives requested but AI is disabled or its provider key is missing. Check AiService:Enabled and the provider credential on the worker.",
            System.Net.HttpStatusCode.ServiceUnavailable);
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
