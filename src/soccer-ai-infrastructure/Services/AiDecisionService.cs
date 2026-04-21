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
        StatisticalModels stats,
        AiAnalysisDto? aiContext = null)
    {
        // 1. Get the "Proposed" decisions from the mathematical rule engine
        var result = await _ruleEngine.Evaluate(context, teamStats, h2h, prediction, stats);

        if (prediction == null) return result;

        if (aiContext != null && !string.IsNullOrWhiteSpace(aiContext.Recommendation))
        {
            _logger.LogInformation("[AiDecision] Persisted analysis found for {Home} vs {Away}. Skipping live AI Judge call.", teamStats.Home.Name, teamStats.Away.Name);
            
            // Re-instantiate decisions from stored analysis
            result.Markets.Over25 = new MarketDecision
            {
                IsQualified = !string.IsNullOrWhiteSpace(aiContext.Over25Summary) || aiContext.Recommendation.Contains("Over 2.5", StringComparison.OrdinalIgnoreCase),
                Confidence = aiContext.Confidence,
                Reason = "Persisted AI Verdict"
            };

            result.Markets.BTTS = new MarketDecision
            {
                IsQualified = !string.IsNullOrWhiteSpace(aiContext.BttsSummary) || aiContext.Recommendation.Contains("BTTS", StringComparison.OrdinalIgnoreCase),
                Confidence = aiContext.Confidence,
                Reason = "Persisted AI Verdict"
            };

            var winnerName = prediction.MatchWinner == "home" ? teamStats.Home.Name : teamStats.Away.Name;
            result.Markets.MatchWinner = new MarketDecision
            {
                IsQualified = aiContext.Recommendation.Contains(winnerName, StringComparison.OrdinalIgnoreCase),
                Confidence = aiContext.Confidence,
                Reason = "Persisted AI Verdict"
            };
            
            if (aiContext.IsTrap)
            {
                result.Trap = new TrapDecision
                {
                    IsTrap = true,
                    Reason = aiContext.TrapReason
                };
            }
        }
        else
        {
            var matchFactsJson = JsonSerializer.Serialize(new
            {
                HomeTeam = teamStats.Home.Name,
                AwayTeam = teamStats.Away.Name,
                HomeStats = teamStats.Home,
                AwayStats = teamStats.Away,
                H2H = h2h,
                Models = new { Over25Prob = prediction.Over25Prob, BTTSProb = prediction.BTTSProb, MatchWinnerProb = prediction.Confidence }
            });

            var winnerLabel = prediction.MatchWinner == "home" ? teamStats.Home.Name : teamStats.Away.Name;
            var proposals = new List<KeyValuePair<string, string>>
            {
                new("over25", $"Prediction: Over 2.5 Goals. Current Mathematical Reason: {result.Markets.Over25.Reason}"),
                new("btts", $"Prediction: Both Teams to Score (Yes). Current Mathematical Reason: {result.Markets.BTTS.Reason}"),
                new("winner", $"Prediction: Match Winner: {winnerLabel}. Current Mathematical Reason: {result.Markets.MatchWinner.Reason}")
            };

            _logger.LogInformation("[AiDecision] No persisted analysis. Validating {Count} markets in batch for {Home} vs {Away}...", proposals.Count, teamStats.Home.Name, teamStats.Away.Name);
            var batchResult = await _aiJudge.ValidateMarketsAsync(matchFactsJson, proposals);

            // Apply results
            if (batchResult.TryGetValue("over25", out var over25Judge))
                result.Markets.Over25 = ApplyJudgeVerdict("Over 2.5", result.Markets.Over25, over25Judge);
                
            if (batchResult.TryGetValue("btts", out var bttsJudge))
                result.Markets.BTTS = ApplyJudgeVerdict("BTTS", result.Markets.BTTS, bttsJudge);

            if (batchResult.TryGetValue("winner", out var winnerJudge))
                result.Markets.MatchWinner = ApplyJudgeVerdict("Match Winner", result.Markets.MatchWinner, winnerJudge);
        }

        // 3. Update overall qualification based on Judge's verdict
        result.Qualification.IsQualified = result.Markets.Over25.IsQualified || 
                                          result.Markets.BTTS.IsQualified || 
                                          result.Markets.MatchWinner.IsQualified;

        if (result.Qualification.IsQualified)
        {
            result.Decision = PredictionDecision.StrongBet; // Promote if AI confirms or overrides
            result.Qualification.Label = "Qualified (Validated by AI Decision Layer)";
        }
        else
        {
            result.Decision = PredictionDecision.NoBet;
            result.Qualification.Label = "Invalidated by AI Decision Layer";
        }

        return result;
    }

    private MarketDecision ApplyJudgeVerdict(string marketName, MarketDecision proposed, DecisionLayerResult judgeResult)
    {
        if (judgeResult.Decision == "OVERRIDE_MODEL")
        {
            _logger.LogWarning("[AiDecision] AI OVERRIDDEN MODEL for {Market}: {Reason}", marketName, judgeResult.Reasoning);
            return new MarketDecision
            {
                IsQualified = true,
                Confidence = Math.Max(proposed.Confidence, judgeResult.Confidence / 100.0),
                Reason = $"OVERRIDE MODEL: {judgeResult.Reasoning}"
            };
        }

        if (judgeResult.Decision == "INVALID")
        {
            _logger.LogWarning("[AiDecision] Market {Market} INVALIDATED: {Reason}", marketName, judgeResult.Reasoning);
            return new MarketDecision
            {
                IsQualified = false,
                Confidence = proposed.Confidence * 0.5,
                Reason = $"JUDGE REJECTED: {judgeResult.Reasoning}"
            };
        }

        if (judgeResult.Decision == "VALID" && proposed.IsQualified)
        {
            _logger.LogInformation("[AiDecision] Market {Market} VALIDATED: {Reason}", marketName, judgeResult.Reasoning);
            return new MarketDecision
            {
                IsQualified = true,
                Confidence = proposed.Confidence,
                Reason = $"JUDGE APPROVED: {judgeResult.Reasoning}"
            };
        }

        return proposed;
    }

    public async Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return await Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }
}
