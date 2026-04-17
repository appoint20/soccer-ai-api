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
    IAiAnalysisService aiService,
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
            logger.LogInformation("[Chat] Empty query received. Falling back to SYSTEM daily combinations for {Date}.", cmd.Date.ToString("yyyy-MM-dd"));
            var dailyQuery = new GetMatchCombinationQuery(cmd.Date, cmd.Language, false);
            var dailyResponse = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(dailyQuery, cancellationToken);
            
            return new CreateChatCombinationResponse 
            { 
                Success = true, 
                Combinations = dailyResponse.Combinations.Take(5).ToList(),
                AiReasoning = $"No criteria specified. Showing top system recommendations for {cmd.Date:yyyy-MM-dd}.",
                Message = $"Here are the top-rated mathematical portfolios for {cmd.Date:yyyy-MM-dd}."
            };
        }

        // 2. Parse natural language into structured intent
        var intent = await aiService.ParseChatIntentAsync(cmd.Query);
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

        // Explicitly decouple these queries from system automation
        intent.SourceType = "USER";

        // 2. Fetch analyzed matches for the target date
        var analysisQuery = new GetMatchAnalysisQuery { Date = cmd.Date, Language = cmd.Language };
        var analysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(analysisQuery, cancellationToken);
        
        if (analysisResponse.Matches == null || analysisResponse.Matches.Count == 0)
        {
            return new CreateChatCombinationResponse 
            { 
                Success = false, 
                Message = "No matches available for analysis today." 
            };
        }

        // Apply Time Filtering if specified
        if (intent.TimeFrame != null)
        {
            if (intent.TimeFrame.StartTime.HasValue)
            {
                analysisResponse.Matches = analysisResponse.Matches
                    .Where(m => m.Time >= intent.TimeFrame.StartTime.Value)
                    .ToList();
            }
            if (intent.TimeFrame.EndTime.HasValue)
            {
                analysisResponse.Matches = analysisResponse.Matches
                    .Where(m => m.Time <= intent.TimeFrame.EndTime.Value)
                    .ToList();
            }

            if (analysisResponse.Matches.Count == 0)
            {
                return new CreateChatCombinationResponse 
                { 
                    Success = false, 
                    Message = "No matches available within the specified time frame." 
                };
            }
        }

        // Apply League Filtering if specified
        if (intent.PreferredLeagues != null && intent.PreferredLeagues.Any())
        {
            // Simple substring match for flexibility (e.g. "England" matches "England: Premier League")
            var prefLeagues = intent.PreferredLeagues.Select(l => l.ToLowerInvariant()).ToList();
            analysisResponse.Matches = analysisResponse.Matches
                .Where(m => prefLeagues.Any(pl => m.League.ToLowerInvariant().Contains(pl)))
                .ToList();

            if (analysisResponse.Matches.Count == 0)
            {
                return new CreateChatCombinationResponse 
                { 
                    Success = false, 
                    Message = $"No matches available for the specified leagues: {string.Join(", ", intent.PreferredLeagues)}" 
                };
            }
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
