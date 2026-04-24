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
/// </summary>
public sealed class AiDecisionService : IDecisionService
{
    private readonly IAiDecisionLayerService _aiJudge;
    private readonly IDecisionService _ruleEngine;
    private readonly ILogger<AiDecisionService> _logger;

    public AiDecisionService(
        IAiDecisionLayerService aiJudge,
        DecisionService ruleEngine,
        ILogger<AiDecisionService> logger)
    {
        _aiJudge = aiJudge;
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
            // ── LEGACY CACHE: Old-style analysis without per-market decisions ──
            // Call AI Decision Layer with enriched data
            _logger.LogInformation("[AiDecision] Persisted analysis found but no decision layer data. Calling AI for {Home} vs {Away}...",
                teamStats.Home.Name, teamStats.Away.Name);

            var aiDecision = await CallDecisionLayer(context, teamStats, h2h, prediction, result);
            if (aiDecision != null)
            {
                ApplyAiDecision(result, aiDecision);
            }
        }
        else
        {
            // ── NO CACHE: Full AI Decision Layer call ──
            _logger.LogInformation("[AiDecision] No persisted analysis. Calling AI Decision Layer for {Home} vs {Away}...",
                teamStats.Home.Name, teamStats.Away.Name);

            var aiDecision = await CallDecisionLayer(context, teamStats, h2h, prediction, result);
            if (aiDecision != null)
            {
                ApplyAiDecision(result, aiDecision);
            }
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
    /// Builds enriched match payload and calls the AI Decision Layer.
    /// </summary>
    private async Task<AiFullDecisionResult?> CallDecisionLayer(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction prediction,
        DecisionServiceResult mathProposal)
    {
        try
        {
            var enrichedPayload = JsonSerializer.Serialize(new
            {
                match = new
                {
                    league = context.LeagueName,
                    date = context.Date
                },
                home_team = new
                {
                    name = teamStats.Home.Name,
                    rank = teamStats.Home.Rank,
                    points = teamStats.Home.Points,
                    played = teamStats.Home.Played,
                    form = teamStats.Home.Form,
                    form_percentage = teamStats.Home.FormPercentage,
                    possession = teamStats.Home.Possession,
                    momentum = teamStats.Home.Momentum,
                    avg_goals_scored_last_3 = teamStats.Home.AvgGoalsScoredLast3,
                    avg_goals_conceded_last_3 = teamStats.Home.AvgGoalsConcededLast3,
                    avg_goals_scored_last_7 = teamStats.Home.AvgGoalsScoredLast7,
                    avg_goals_conceded_last_7 = teamStats.Home.AvgGoalsConcededLast7,
                    attack_strength = teamStats.Home.AttackStrength,
                    defensive_strength = teamStats.Home.DefensiveStrength,
                    clean_sheet_rate = teamStats.Home.CleanSheetRate,
                    win_rate = teamStats.Home.WinRate,
                    btts_rate_last_3 = teamStats.Home.BTTSRateLast3,
                    over25_rate_last_3 = teamStats.Home.Over25RateLast3
                },
                away_team = new
                {
                    name = teamStats.Away.Name,
                    rank = teamStats.Away.Rank,
                    points = teamStats.Away.Points,
                    played = teamStats.Away.Played,
                    form = teamStats.Away.Form,
                    form_percentage = teamStats.Away.FormPercentage,
                    possession = teamStats.Away.Possession,
                    momentum = teamStats.Away.Momentum,
                    avg_goals_scored_last_3 = teamStats.Away.AvgGoalsScoredLast3,
                    avg_goals_conceded_last_3 = teamStats.Away.AvgGoalsConcededLast3,
                    avg_goals_scored_last_7 = teamStats.Away.AvgGoalsScoredLast7,
                    avg_goals_conceded_last_7 = teamStats.Away.AvgGoalsConcededLast7,
                    attack_strength = teamStats.Away.AttackStrength,
                    defensive_strength = teamStats.Away.DefensiveStrength,
                    clean_sheet_rate = teamStats.Away.CleanSheetRate,
                    win_rate = teamStats.Away.WinRate,
                    btts_rate_last_3 = teamStats.Away.BTTSRateLast3,
                    over25_rate_last_3 = teamStats.Away.Over25RateLast3
                },
                h2h = h2h != null ? new
                {
                    matches_analyzed = h2h.MatchesAnalyzed,
                    btts_rate = h2h.BTTSRate,
                    over25_rate = h2h.Over25Rate,
                    avg_total_goals = h2h.AvgTotalGoals,
                    home_win_rate = h2h.HomeWinRate,
                    away_win_rate = h2h.AwayWinRate,
                    draw_rate = h2h.DrawRate
                } : null,
                model_probabilities = new
                {
                    over25 = prediction.Over25Prob,
                    btts = prediction.BTTSProb,
                    home_win = prediction.HomeProb,
                    away_win = prediction.AwayProb,
                    match_winner = prediction.MatchWinner,
                    confidence = prediction.Confidence
                },
                odds = new
                {
                    home = context.OddsHome,
                    draw = context.OddsDraw,
                    away = context.OddsAway,
                    over25 = context.OddsOver25,
                    btts = context.OddsBttsYes
                },
                math_engine_proposals = new
                {
                    over25_qualified = mathProposal.Markets.Over25.IsQualified,
                    over25_reason = mathProposal.Markets.Over25.Reason,
                    btts_qualified = mathProposal.Markets.BTTS.IsQualified,
                    btts_reason = mathProposal.Markets.BTTS.Reason,
                    low_scoring_qualified = mathProposal.Markets.LowScoring.IsQualified,
                    low_scoring_reason = mathProposal.Markets.LowScoring.Reason,
                    goals23_qualified = mathProposal.Markets.TwoToThreeGoals.IsQualified,
                    winner_qualified = mathProposal.Markets.MatchWinner.IsQualified,
                    trap = new { is_trap = mathProposal.Trap.IsTrap, reason = mathProposal.Trap.Reason }
                }
            });

            return await _aiJudge.EvaluateMatchAsync(enrichedPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiDecision] Failed to call AI Decision Layer.");
            return null;
        }
    }

    /// <summary>
    /// Apply AI decision layer results to the market decisions.
    /// </summary>
    private void ApplyAiDecision(DecisionServiceResult result, AiFullDecisionResult ai)
    {
        result.Markets.Over25 = ToMarketDecision(ai.Over25, "Over 2.5");
        result.Markets.BTTS = ToMarketDecision(ai.Btts, "BTTS");
        result.Markets.LowScoring = ToMarketDecision(ai.Under25, "Under 2.5");
        result.Markets.TwoToThreeGoals = ToMarketDecision(ai.Goals23, "2-3 Goals");
        result.Markets.MatchWinner = ToMarketDecision(ai.HomeWin.Qualified ? ai.HomeWin : ai.AwayWin, "Match Winner");

        if (ai.Trap.IsTrap)
        {
            result.Trap = new TrapDecision { IsTrap = true, Reason = ai.Trap.Reason };
        }

        _logger.LogInformation("[AiDecision] AI Best Bet: {BestBet} (Confidence: {Conf}%)", ai.BestBet, ai.OverallConfidence);
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

    private static MarketDecision ToMarketDecision(AiMarketDecision ai, string label) => new()
    {
        IsQualified = ai.Qualified && ai.Confidence >= 60,
        Confidence = ai.Confidence / 100.0,
        Reason = $"AI: {ai.Reasoning}"
    };

    public async Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head)
    {
        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        return await Evaluate(new MatchContext(), teamStats, head2head, null, new StatisticalModels());
    }
}
