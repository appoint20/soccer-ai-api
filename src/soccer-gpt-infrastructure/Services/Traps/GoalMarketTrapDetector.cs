using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_infrastructure.Services.Traps;

namespace soccer_gpt_infrastructure.Services.Traps;

public class GoalMarketTrapDetector : ITrapDetector
{
    public string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        if (match.Odds == null || match.Odds.Over25 == 0) return null;

        var traps = new List<string>();

        // 1. Over 2.5 Trap
        // Metric: Market expects GOALS (Low Odds on Over 2.5), but Model expects TIGHT game.
        // Threshold: Odds < 1.65 (Implied > 60%) vs Model < 45%
        
        decimal marketOver25Prob = 1.0m / match.Odds.Over25;
        double modelOver25Prob = analytics.Probabilities.Over25;

        if (match.Odds.Over25 < 1.65m && modelOver25Prob < 0.45)
        {
            return $"Over 2.5 Trap: Market expects goals ({match.Odds.Over25} => {marketOver25Prob:P0}) but Model predicts tight game ({modelOver25Prob:P0}).";
        }

        // 3. Over 1.5 Validity Check
        // If Model says Over 1.5 is < 60%, it is a risky game for any Over betting, even if odds are low.
        if (analytics.Probabilities.Over15 < 0.60 && match.Odds.Over25 < 1.50m)
        {
             return $"Goal Trap: Market expects goal fest, but Model sees risk (Over 1.5 only {analytics.Probabilities.Over15:P0}).";
        }

        return null;
    }
}
