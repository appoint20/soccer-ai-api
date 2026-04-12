using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.Application.Features.Combinations;

public class CreateChatCombinationHandler(
    IMediator mediator,
    IGeminiAnalysisService geminiService,
    IChatCombinationEngine engine,
    ILogger<CreateChatCombinationHandler> logger) 
    : ICommandHandler<CreateChatCombinationCommand, CreateChatCombinationResponse>
{
    public async Task<CreateChatCombinationResponse> Handle(IReceiveContext<CreateChatCombinationCommand> context, CancellationToken cancellationToken)
    {
        var cmd = context.Message;
        logger.LogInformation("[Chat] Received natural language request: {Query}", cmd.Query);

        // 1. Check for empty query - Fallback to SYSTEM Daily Combinations
        if (string.IsNullOrWhiteSpace(cmd.Query))
        {
            logger.LogInformation("[Chat] Empty query received. Falling back to SYSTEM daily combinations.");
            var dailyQuery = new GetMatchCombinationQuery(DateTimeOffset.UtcNow, cmd.Language, false);
            var dailyResponse = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(dailyQuery, cancellationToken);
            
            return new CreateChatCombinationResponse 
            { 
                Success = true, 
                Combinations = dailyResponse.Combinations.Take(5).ToList(),
                AiReasoning = "No criteria specified. Showing today's top system recommendations.",
                Message = "Here are today's top-rated mathematical portfolios."
            };
        }

        // 2. Parse natural language into structured intent
        var intent = await geminiService.ParseChatIntentAsync(cmd.Query);
        if (intent == null)
        {
            return new CreateChatCombinationResponse 
            { 
                Success = false, 
                Message = "Could not understand your request. Please try again with details about matches and odds." 
            };
        }

        logger.LogInformation("[Chat] Extracted Intent: Markets={Markets}, MinOdds={Odds}", 
            string.Join(",", intent.PreferredMarkets), intent.MinTotalOdds);

        // 2. Fetch today's analyzed matches
        var analysisQuery = new GetMatchAnalysisQuery { Date = DateTimeOffset.UtcNow, Language = cmd.Language };
        var analysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(analysisQuery, cancellationToken);
        
        if (analysisResponse.Matches == null || analysisResponse.Matches.Count == 0)
        {
            return new CreateChatCombinationResponse 
            { 
                Success = false, 
                Message = "No matches available for analysis today." 
            };
        }

        // 3. Generate and rank combinations using the engine
        var combinations = engine.GenerateCombinations(analysisResponse.Matches, intent);

        if (combinations.Count == 0)
        {
            return new CreateChatCombinationResponse 
            { 
                Success = false, 
                Message = "I found matches, but none of them could form a valid parlay that meets your odds and market criteria. Try relaxing your constraints!" 
            };
        }

        return new CreateChatCombinationResponse
        {
            Success = true,
            Combinations = combinations,
            AiReasoning = intent.Reasoning,
            Message = $"Found {combinations.Count} combinations based on your request."
        };
    }
}
