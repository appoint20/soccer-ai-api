using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// AI-driven implementation of IDecisionService.
/// Uses AiDecisionLayerService to validate mathematical predictions against raw facts.
/// </summary>
public sealed class AiDecisionService : IDecisionService
{
    private readonly IAiDecisionLayerService _aiJudge;
    private readonly IDecisionService _ruleEngine; // Fallback or base logic
    private readonly ILogger<AiDecisionService> _logger;

    public AiDecisionService(
        IAiDecisionLayerService aiJudge,
        DecisionService ruleEngine, // We inject the concrete rule-based service as the 'proposer'
        ILogger<AiDecisionService> _logger)
    {
        _aiJudge = aiJudge;
        _ruleEngine = ruleEngine;
        this._logger = _logger;
    }

    public async Task<DecisionServiceResult> Evaluate(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction? prediction,
        StatisticalModels stats)
    {
        // 1. Get the "Proposed" decisions from the mathematical rule engine
        var result = await _ruleEngine.Evaluate(context, teamStats, h2h, prediction, stats);

        if (prediction == null) return result;

        // 2. We only want to "Judge" the markets that the math engine actually LIKED (Qualified)
        // or we judge the overall Match Winner if it's high confidence.
        
        var matchFactsJson = JsonSerializer.Serialize(new
        {
            HomeTeam = teamStats.Home.Name,
            AwayTeam = teamStats.Away.Name,
            HomeStats = teamStats.Home,
            AwayStats = teamStats.Away,
            H2H = h2h
        });

        // Loop through markets and ask the Judge for verification
        if (result.Markets.Over25.IsQualified)
        {
            result.Markets.Over25 = await ValidateMarket(matchFactsJson, "Over 2.5 Goals", result.Markets.Over25);
        }
        
        if (result.Markets.BTTS.IsQualified)
        {
            result.Markets.BTTS = await ValidateMarket(matchFactsJson, "Both Teams to Score (Yes)", result.Markets.BTTS);
        }

        if (result.Markets.MatchWinner.IsQualified)
        {
            var winnerLabel = prediction.MatchWinner == "home" ? teamStats.Home.Name : teamStats.Away.Name;
            result.Markets.MatchWinner = await ValidateMarket(matchFactsJson, $"Match Winner: {winnerLabel}", result.Markets.MatchWinner);
        }

        // 3. Update overall qualification based on Judge's verdict
        result.Qualification.IsQualified = result.Markets.Over25.IsQualified || 
                                          result.Markets.BTTS.IsQualified || 
                                          result.Markets.MatchWinner.IsQualified;

        if (!result.Qualification.IsQualified)
        {
            result.Decision = PredictionDecision.NoBet;
            result.Qualification.Label = "Invalidated by AI Decision Layer";
        }

        return result;
    }

    private async Task<MarketDecision> ValidateMarket(string factsJson, string marketName, MarketDecision proposed)
    {
        _logger.LogInformation("[AiDecision] Validating {Market}...", marketName);
        
        var judgeResult = await _aiJudge.ValidatePredictionAsync(factsJson, $"Prediction: {marketName}. Reasoning: {proposed.Reason}");

        if (judgeResult.Decision == "INVALID")
        {
            _logger.LogWarning("[AiDecision] Market {Market} INVALIDATED: {Reason}", marketName, judgeResult.Reason);
            return new MarketDecision
            {
                IsQualified = false,
                Confidence = proposed.Confidence * 0.5, // Reduce confidence
                Reason = $"JUDGE REJECTED: {judgeResult.Reason}"
            };
        }

        if (judgeResult.Decision == "VALID")
        {
            _logger.LogInformation("[AiDecision] Market {Market} VALIDATED: {Reason}", marketName, judgeResult.Reason);
            return new MarketDecision
            {
                IsQualified = true,
                Confidence = proposed.Confidence,
                Reason = $"JUDGE APPROVED: {judgeResult.Reason}"
            };
        }

        return proposed; // Keep original if unsure
    }

    public async Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return await Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }
}
