using Microsoft.Extensions.Logging;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Filters;

/// <summary>
/// Aggressive defensive filter that BLOCKS bad bets based on empirical failure analysis.
/// All rules are derived from analyzing 254 failures across 580 bets over 15 weeks.
/// </summary>
public class DefensiveBetFilter
{
    private readonly ILogger<DefensiveBetFilter> _logger;
    
    public DefensiveBetFilter(ILogger<DefensiveBetFilter> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Apply all defensive filters for a given market.
    /// Returns BetDecision indicating if bet is allowed or blocked with reason.
    /// </summary>
    public BetDecision ApplyFilters(MatchPrediction match, string market)
    {
        return market switch
        {
            "Over 2.5 Goals" or "Over25" => ApplyOver25Filters(match),
            "BTTS Yes" or "BTTS" => ApplyBTTSFilters(match),
            _ => BetDecision.Allow(market)
        };
    }
    
    #region Over 2.5 Filters
    
    private BetDecision ApplyOver25Filters(MatchPrediction match)
    {
        // Filter 1: 0-0 Draw Detector
        // Empirical: 0-0 draws increased after initial filters (10.8% → 12.4%)
        var draw00Decision = Check00DrawRisk(match);
        if (!draw00Decision.IsBetAllowed)
        {
            _logger.LogInformation("Over 2.5 BLOCKED: {Reason}", draw00Decision.BlockReason);
            return draw00Decision;
        }
        
        // Filter 2: 2-Goal Trap
        // Empirical: 54.3% of Over 2.5 failures end with EXACTLY 2 goals (mainly 2-0)
        // Strong favorites win 2-0 and stop attacking due to game management
        if (match.Over25Probability >= 0.55 && match.Over25Probability <= 0.68)
        {
            if (match.XGDiff >= 0.6 && 
                (match.Home.CleanSheetRateLast10 >= 0.40 || match.Away.CleanSheetRateLast10 >= 0.40) &&
                match.FavoriteWinProbability >= 0.55)
            {
                var reason = $"2-goal trap: Favorite likely to win 2-0 and stop (XG diff: {match.XGDiff:F2}, " +
                           $"Clean sheet rate: {Math.Max(match.Home.CleanSheetRateLast10, match.Away.CleanSheetRateLast10):P0})";
                
                _logger.LogInformation("Over 2.5 BLOCKED: {Reason}", reason);
                return BetDecision.Block("Over 2.5 Goals", reason);
            }
        }
        
        return BetDecision.Allow("Over 2.5 Goals");
    }
    
    #endregion
    
    #region BTTS Filters
    
    private BetDecision ApplyBTTSFilters(MatchPrediction match)
    {
        // Filter 1: 0-0 Draw Detector
        // Empirical: 0-0 draws increased (10.8% → 12.4%)
        var draw00Decision = Check00DrawRisk(match);
        if (!draw00Decision.IsBetAllowed)
        {
            _logger.LogInformation("BTTS BLOCKED: {Reason}", draw00Decision.BlockReason);
            return draw00Decision;
        }
        
        // Filter 2: Defensive Shutdown
        // Empirical: 36.5% of BTTS failures are 1-0 or 0-1 (one team completely blanked)
        // Teams with high failure-to-score rates or strong defenses shut out opponents
        if (match.Home.FailedToScoreRateLast10 >= 0.35)
        {
            var reason = $"Home failed to score in {match.Home.FailedToScoreRateLast10:P0} of last 10 matches";
            _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
            return BetDecision.Block("BTTS Yes", reason);
        }
        
        if (match.Away.FailedToScoreRateLast10 >= 0.35)
        {
            var reason = $"Away failed to score in {match.Away.FailedToScoreRateLast10:P0} of last 10 matches";
            _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
            return BetDecision.Block("BTTS Yes", reason);
        }
        
        // Strong home defense vs weak away attack
        if (match.Home.CleanSheetRateLast10 >= 0.40 && match.AwayXG < 1.0)
        {
            var reason = $"Home has {match.Home.CleanSheetRateLast10:P0} clean sheet rate vs low away attack (xG: {match.AwayXG:F2})";
            _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
            return BetDecision.Block("BTTS Yes", reason);
        }
        
        // Strong away defense vs weak home attack
        if (match.Away.CleanSheetRateLast10 >= 0.40 && match.HomeXG < 1.0)
        {
            var reason = $"Away has {match.Away.CleanSheetRateLast10:P0} clean sheet rate vs low home attack (xG: {match.HomeXG:F2})";
            _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
            return BetDecision.Block("BTTS Yes", reason);
        }
        
        // Filter 3: One-Sided Dominance
        // Empirical: 25.3% of BTTS failures are 3+ goal margin games (3-0, 4-0, 5-0)
        // Dominant favorites crush weak opponents without conceding
        if (match.XGDiff >= 1.2 && 
            match.FavoriteWinProbability >= 0.65 && 
            match.UnderdogAvgGoalsForLast10 < 1.0)
        {
            var reason = $"One-sided dominance: XG diff {match.XGDiff:F2}, favorite {match.FavoriteWinProbability:P0}, " +
                       $"underdog scored only {match.UnderdogAvgGoalsForLast10:F2} goals/game (risk of 3-0, 4-0)";
            
            _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
            return BetDecision.Block("BTTS Yes", reason);
        }
        
        // Filter 4: Bundesliga-Specific Rules
        // Empirical: Bundesliga (D1) had 44 BTTS failures (21.7% of all failures)
        // German teams either score heavily or shut out opponents completely
        if (match.League == "D1")
        {
            // Moderate mismatch in Bundesliga is very risky for BTTS
            if (match.XGDiff >= 1.0)
            {
                var reason = $"Bundesliga moderate mismatch: XG diff {match.XGDiff:F2} (high risk of one-sided game)";
                _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
                return BetDecision.Block("BTTS Yes", reason);
            }
            
            // Very strong defense in Bundesliga often keeps clean sheets
            if (match.Home.CleanSheetRateLast10 >= 0.45 || match.Away.CleanSheetRateLast10 >= 0.45)
            {
                var csRate = Math.Max(match.Home.CleanSheetRateLast10, match.Away.CleanSheetRateLast10);
                var reason = $"Bundesliga strong defense: {csRate:P0} clean sheet rate (German teams defend well)";
                _logger.LogInformation("BTTS BLOCKED: {Reason}", reason);
                return BetDecision.Block("BTTS Yes", reason);
            }
        }
        
        return BetDecision.Allow("BTTS Yes");
    }
    
    #endregion
    
    #region Common Filters
    
    /// <summary>
    /// 0-0 Draw Detector - applies to BOTH Over 2.5 and BTTS
    /// Empirical: 0-0 draws increased after initial filters (10.8% → 12.4% for BTTS)
    /// Both teams have high draw rates and low expected goals = stalemate risk
    /// </summary>
    private BetDecision Check00DrawRisk(MatchPrediction match)
    {
        if (match.Home.DrawRate >= 0.30 && 
            match.Away.DrawRate >= 0.30 && 
            match.HomeXG <= 1.1 && 
            match.AwayXG <= 1.1)
        {
            var reason = $"High 0-0 draw risk: Both teams draw {match.Home.DrawRate:P0}/{match.Away.DrawRate:P0}, " +
                       $"low xG ({match.HomeXG:F2}/{match.AwayXG:F2})";
            
            return BetDecision.Block("", reason);
        }
        
        return BetDecision.Allow("");
    }
    
    #endregion
}
