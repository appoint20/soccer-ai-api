using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

using SoccerAi.Application.Exceptions;

namespace SoccerAi.Application.Features.Combinations;

/// <summary>
/// Handles combination portfolio generation requests directly from JSON Analysis payloads.
///
/// Orchestrates the combination pipeline:
/// 1. Fetches full Match Analysis JSON objects via Mediator GetMatchAnalysisQuery 
/// 2. Ranks matches by highest overall internal confidence 
/// 3. Batches matches and yields straight to AI service
/// 4. Returns structured combination DTOs combining the raw JSON elements
///
/// Refactored to bypass mathematical portfolio generators.
/// </summary>
public class GetMatchCombinationHandler(
    IMediator mediator,
    IChatCombinationEngine combinationEngine,
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("[Combinations] Generating SYSTEM portfolios for {Date}. Pure live math.", query.Date.ToString("yyyy-MM-dd"));

        // Step 1: Request live Match Analysis from the orchestrator
        var analysisQuery = new GetMatchAnalysisQuery { Date = query.Date, Language = query.Language };
        var analysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(analysisQuery, cancellationToken);

        if (analysisResponse.Matches == null || analysisResponse.Matches.Count == 0)
        {
            return new GetMatchCombinationResponse([]);
        }

        // Step 2: Determine Intent (SYSTEM or USER)
        bool isUser = !string.IsNullOrWhiteSpace(query.UserMessage);
        var intent = new ChatCombinationIntent
        {
            SourceType = isUser ? "USER" : "SYSTEM",
            Refresh = query.Refresh,
            UserMessage = query.UserMessage
        };

        if (isUser)
        {
            // Optional: Parse natural language into structured intent if the engine needs specific filters
            // For now, the engine (AI) will handle the query directly inside BuildCombinationsAsync if we adapt it.
            // But the current BuildCombinationsAsync doesn't take the user message.
            // I should update IAiAnalysisService.BuildCombinationsAsync to accept the context/message.
            logger.LogInformation("[Combinations] USER chat request: {Query}", query.UserMessage);
        }

        // Step 3: Call the AI-driven Engine
        var portfolios = await combinationEngine.GenerateCombinationsAsync(analysisResponse.Matches, intent);

        // Re-index for consistent IDs
        for (int i = 0; i < portfolios.Count; i++)
        {
            portfolios[i].CombinationId = i + 1;
        }

        return new GetMatchCombinationResponse(portfolios);
    }
}
