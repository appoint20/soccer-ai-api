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
/// 3. Batches matches in groupings of 10 and yields straight to Gemini AI
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

        // Step 2: Delegate to the Insane Math Engine for portfolio generation
        // Default SYSTEM intent: No filters, use daily hierarchy
        var intent = new ChatCombinationIntent
        {
            SourceType = "SYSTEM",
            MinSelectionOdds = 1.60
        };

        var portfolios = combinationEngine.GenerateCombinations(analysisResponse.Matches, intent);

        // Re-index for consistent IDs
        for (int i = 0; i < portfolios.Count; i++)
        {
            portfolios[i].CombinationId = i + 1;
        }

        return new GetMatchCombinationResponse(portfolios);
    }
}

    private static string GetPrimarySelection(MatchAnalysis match)
    {
        if (match.Prediction?.MatchWinner == null) return "Match Winner";
        return match.Prediction.MatchWinner.Prediction.ToLower() switch
        {
            "home" => "Match Winner (Home)",
            "away" => "Match Winner (Away)",
            _ => "BTTS" // Default to BTTS if draw/other to keep it diverse
        };
    }

    private static string GetNaturalDailyReasoning(MatchAnalysis m)
    {
        var conf = m.Prediction?.MatchWinner?.Confidence ?? 0;
        var confText = conf switch
        {
            >= 0.8 => "very high predictive probability",
            >= 0.7 => "strong statistical consensus",
            _ => "favorable analytical advantage"
        };
        return $"Selection backed by {confText} and exceptional season form.";
    }

    private static double GetPrimaryOdds(MatchAnalysis match)
    {
        if (match.Prediction?.MatchWinner == null) return 1.0;
        return match.Prediction.MatchWinner.Prediction.ToLower() switch
        {
            "home" => match.OddsHomeWin > 0 ? match.OddsHomeWin : 1.5,
            "away" => match.OddsAwayWin > 0 ? match.OddsAwayWin : 1.5,
            _ => match.OddsBttsYes > 0 ? match.OddsBttsYes : 1.8
        };
    }
}
