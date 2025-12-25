using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Filters;

public class PoissonFailureFilters : IPoissonFailureFilters
{
    private readonly ILogger<PoissonFailureFilters> _logger;
    
    // Filter thresholds - 2-Goal Trap
    private const double TWO_GOAL_TRAP_MIN_CONF = 0.50;
    private const double TWO_GOAL_TRAP_MAX_CONF = 0.55;
    private const double TWO_GOAL_TRAP_XG_DIFF = 0.8;
    private const double TWO_GOAL_TRAP_MAX_ODDS = 1.55;
    private const double TWO_GOAL_TRAP_CAP = 0.54;
    
    // Filter thresholds - Defensive Shutdown
    private const double CLEAN_SHEET_THRESHOLD = 0.40;
    private const double FAILED_TO_SCORE_THRESHOLD = 0.35;
    private const double BTTS_DEFENSIVE_CAP = 0.60;
    
    // Filter thresholds - One-Sided Game
    private const double ONE_SIDED_XG_DIFF = 1.1;
    private const double ONE_SIDED_MAX_ODDS = 1.50;
    
    // League-specific penalties
    private const double BUNDESLIGA_PENALTY = 0.95;
    
    public PoissonFailureFilters(ILogger<PoissonFailureFilters> logger)
    {
        _logger = logger;
    }
    
    public FilterResult ApplyOver25Filters(MatchContext ctx)
    {
        var confidence = ctx.Over25Probability;
        var reasons = new List<string>();
        
        // Filter 1: 2-Goal Trap Detection
        // Favorites win 2-0 and stop attacking (51% of Over 2.5 failures)
        if (confidence >= TWO_GOAL_TRAP_MIN_CONF && 
            confidence <= TWO_GOAL_TRAP_MAX_CONF &&
            ctx.XGDiff >= TWO_GOAL_TRAP_XG_DIFF &&
            ctx.FavoriteOdds <= TWO_GOAL_TRAP_MAX_ODDS)
        {
            reasons.Add($"2-goal trap detected: Favorite (odds {ctx.FavoriteOdds:F2}) likely to win 2-0 and stop");
            confidence = Math.Min(confidence, TWO_GOAL_TRAP_CAP);
            
            _logger.LogInformation(
                "Over 2.5 capped by 2-goal trap: {Original:P1} → {Capped:P1} | XGDiff: {XGDiff:F2} | FavOdds: {Odds:F2}",
                ctx.Over25Probability, confidence, ctx.XGDiff, ctx.FavoriteOdds);
        }
        
        if (reasons.Any())
            return FilterResult.Cap(confidence, reasons.ToArray());
        
        return FilterResult.Allow(confidence);
    }
    
    public FilterResult ApplyBTTSFilters(MatchContext ctx)
    {
        var confidence = ctx.BTTSProbability;
        var reasons = new List<string>();
        
        // Filter 1: Defensive Shutdown Detection
        // 36% of BTTS failures are 1-0 or 0-1 (one team blanked)
        bool defensiveShutdown = false;
        
        if (ctx.HomeCleanSheetRateLast10 >= CLEAN_SHEET_THRESHOLD)
        {
            reasons.Add($"Home has {ctx.HomeCleanSheetRateLast10:P0} clean sheet rate");
            defensiveShutdown = true;
        }
        
        if (ctx.AwayCleanSheetRateLast10 >= CLEAN_SHEET_THRESHOLD)
        {
            reasons.Add($"Away has {ctx.AwayCleanSheetRateLast10:P0} clean sheet rate");
            defensiveShutdown = true;
        }
        
        if (ctx.HomeFailedToScoreRateLast10 >= FAILED_TO_SCORE_THRESHOLD)
        {
            reasons.Add($"Home failed to score in {ctx.HomeFailedToScoreRateLast10:P0} of matches");
            defensiveShutdown = true;
        }
        
        if (ctx.AwayFailedToScoreRateLast10 >= FAILED_TO_SCORE_THRESHOLD)
        {
            reasons.Add($"Away failed to score in {ctx.AwayFailedToScoreRateLast10:P0} of matches");
            defensiveShutdown = true;
        }
        
        if (defensiveShutdown)
        {
            confidence = Math.Min(confidence, BTTS_DEFENSIVE_CAP);
            _logger.LogInformation("BTTS capped by defensive shutdown: {Original:P1} → {Capped:P1} | {Reasons}",
                ctx.BTTSProbability, confidence, string.Join(", ", reasons));
        }
        
        // Filter 2: One-Sided Game Detection (BLOCKS entirely)
        // 28% of BTTS failures are dominant wins (3-0, 4-0, 5-0)
        if (ctx.XGDiff >= ONE_SIDED_XG_DIFF && ctx.FavoriteOdds <= ONE_SIDED_MAX_ODDS)
        {
            var blockReason = $"One-sided game: XG diff {ctx.XGDiff:F2}, favorite odds {ctx.FavoriteOdds:F2} (risk of 3-0, 4-0)";
            reasons.Add(blockReason);
            
            _logger.LogInformation("BTTS BLOCKED by one-sided game: {Home}XG {HomeXG:F2} vs {Away}XG {AwayXG:F2} | Odds: {Odds:F2}",
                "Home", ctx.HomeXG, "Away", ctx.AwayXG, ctx.FavoriteOdds);
                
            return FilterResult.Block(string.Join(" | ", reasons));
        }
        
        // Filter 3: Bundesliga Risk Penalty
        // Bundesliga (D1) has 44 BTTS failures (21.7% of all failures)
        if (ctx.League == "D1")
        {
            var originalConfidence = confidence;
            confidence *= BUNDESLIGA_PENALTY;
            reasons.Add($"Bundesliga risk penalty: -{(1 - BUNDESLIGA_PENALTY) * 100:F0}%");
            
            _logger.LogInformation("BTTS penalized for Bundesliga: {Original:P1} → {Final:P1}",
                originalConfidence, confidence);
        }
        
        if (reasons.Any())
            return FilterResult.Cap(confidence, reasons.ToArray());
            
        return FilterResult.Allow(confidence);
    }
}
