using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// AI-driven implementation of IDecisionService.
/// 1. Gets mathematical proposals from DecisionService (rule engine).
/// 2. Checks if persisted AI decisions exist in the DB (via AiAnalysisDto).
/// 3. If not, calls the AI Decision Layer with enriched match data.
/// 4. Merges AI decisions into the final result.
/// Persistence of decisions is handled by MatchAnalysisService (which has the fixtureId).
/// </summary>
public sealed class AiDecisionService : IDecisionService
{
    private readonly IDecisionService _ruleEngine;
    private readonly ILogger<AiDecisionService> _logger;

    public AiDecisionService(
        DecisionService ruleEngine,
        ILogger<AiDecisionService> logger)
    {
        _ruleEngine = ruleEngine;
        _logger = logger;
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

        if (aiContext != null && aiContext.HasDecisionLayer)
        {
            // ── CACHE HIT: Use persisted AI market decisions ──
            _logger.LogInformation("[AiDecision] Using persisted AI decisions for {Home} vs {Away}.",
                teamStats.Home.Name, teamStats.Away.Name);

            ApplyPersistedDecisions(result, aiContext, prediction, teamStats);
        }
        else if (aiContext != null && !string.IsNullOrWhiteSpace(aiContext.Recommendation))
        {
            // ── LEGACY CACHE: Use rule engine as fallback since Decision Layer is missing ──
            _logger.LogInformation("[AiDecision] Persisted analysis found but no decision layer data. Using Rule Engine for {Home} vs {Away}.",
                teamStats.Home.Name, teamStats.Away.Name);
        }
        else
        {
            // ── NO CACHE: Rule Engine ──
            _logger.LogInformation("[AiDecision] No persisted analysis. Using Rule Engine for {Home} vs {Away}.",
                teamStats.Home.Name, teamStats.Away.Name);
        }

        // Update overall qualification
        result.Qualification.IsQualified = result.Markets.Over25.IsQualified ||
                                           result.Markets.BTTS.IsQualified ||
                                           result.Markets.LowScoring.IsQualified ||
                                           result.Markets.TwoToThreeGoals.IsQualified ||
                                           result.Markets.MatchWinner.IsQualified;

        if (result.Qualification.IsQualified)
        {
            result.Decision = PredictionDecision.StrongBet;
            result.Qualification.Label = "Qualified (AI Decision Layer)";
        }
        else
        {
            result.Decision = PredictionDecision.NoBet;
            result.Qualification.Label = "Not qualified (AI Decision Layer)";
        }

        return result;
    }

    /// <summary>
    /// Apply persisted AI market decisions from the database.
    /// </summary>
    private void ApplyPersistedDecisions(
        DecisionServiceResult result,
        AiAnalysisDto ai,
        WeightedPrediction prediction,
        TeamStatsResponse teamStats)
    {
        result.Markets.Over25 = new MarketDecision
        {
            IsQualified = ai.AiOver25Qualified,
            Confidence = ai.Confidence,
            Reason = !string.IsNullOrWhiteSpace(ai.Over25Summary) ? ai.Over25Summary : "AI Decision Layer"
        };

        result.Markets.BTTS = new MarketDecision
        {
            IsQualified = ai.AiBttsQualified,
            Confidence = ai.Confidence,
            Reason = !string.IsNullOrWhiteSpace(ai.BttsSummary) ? ai.BttsSummary : "AI Decision Layer"
        };

        result.Markets.LowScoring = new MarketDecision
        {
            IsQualified = ai.AiUnder25Qualified,
            Confidence = ai.Confidence,
            Reason = !string.IsNullOrWhiteSpace(ai.Under25Summary) ? ai.Under25Summary : "AI Decision Layer"
        };

        result.Markets.TwoToThreeGoals = new MarketDecision
        {
            IsQualified = ai.AiGoals23Qualified,
            Confidence = ai.Confidence,
            Reason = "AI Decision Layer"
        };

        var winnerName = prediction.MatchWinner == "home" ? teamStats.Home.Name : teamStats.Away.Name;
        result.Markets.MatchWinner = new MarketDecision
        {
            IsQualified = ai.AiHomeWinQualified || ai.AiAwayWinQualified,
            Confidence = ai.Confidence,
            Reason = prediction.MatchWinner == "home"
                ? (!string.IsNullOrWhiteSpace(ai.HomeWinSummary) ? ai.HomeWinSummary : "AI Decision Layer")
                : (!string.IsNullOrWhiteSpace(ai.AwayWinSummary) ? ai.AwayWinSummary : "AI Decision Layer")
        };

        if (ai.IsTrap)
        {
            result.Trap = new TrapDecision { IsTrap = true, Reason = ai.TrapReason };
        }
    }
    public async Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return await Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }
}
