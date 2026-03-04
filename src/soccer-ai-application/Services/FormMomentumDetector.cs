namespace SoccerAi.Application.Services;

using SoccerAi.Application.Models;

/// <summary>
/// Detects form momentum shifts — when a team's recent 3-match trend
/// diverges from their last 7-match average.
///
/// Example: A team with 30% BTTS over last 7 games, but 67% BTTS
/// in last 3 games → they're trending toward more open, goal-scoring games.
/// The model should recognize this shift and boost goal market probabilities.
///
/// This catches teams that are "turning a corner" — starting to score/concede
/// more after a dry spell, or vice versa.
/// </summary>
public static class FormMomentumDetector
{
    /// <summary>
    /// Minimum improvement from L7 to L3 to trigger a momentum boost.
    /// 0.15 = the last-3 rate must be at least 15pp above last-7 rate.
    /// </summary>
    private const double MinMomentumShift = 0.15;

    /// <summary>Maximum boost from momentum detection.</summary>
    private const double MaxBoost = 0.08;

    /// <summary>
    /// Weight applied to the momentum signal.
    /// </summary>
    private const double MomentumWeight = 0.35;

    /// <summary>
    /// Calculate combined Over 2.5 momentum boost from both teams' form trends.
    /// </summary>
    public static double Over25MomentumBoost(TeamStatsResponse stats)
    {
        double homeShift = stats.Home.Over25RateLast3 - stats.Home.Over25RateLast7;
        double awayShift = stats.Away.Over25RateLast3 - stats.Away.Over25RateLast7;

        // Average momentum of both teams
        double avgShift = (homeShift + awayShift) / 2.0;

        if (avgShift < MinMomentumShift) return 0;

        double boost = avgShift * MomentumWeight;
        return Math.Min(boost, MaxBoost);
    }

    /// <summary>
    /// Calculate combined BTTS momentum boost from both teams' form trends.
    /// </summary>
    public static double BTTSMomentumBoost(TeamStatsResponse stats)
    {
        double homeShift = stats.Home.BTTSRateLast3 - stats.Home.BTTSRateLast7;
        double awayShift = stats.Away.BTTSRateLast3 - stats.Away.BTTSRateLast7;

        double avgShift = (homeShift + awayShift) / 2.0;

        if (avgShift < MinMomentumShift) return 0;

        double boost = avgShift * MomentumWeight;
        return Math.Min(boost, MaxBoost);
    }

    /// <summary>
    /// Detect if either team's attack is trending upward (scoring momentum).
    /// Looks at avg goals scored in L3 vs L7.
    /// </summary>
    public static double AttackMomentumBoost(TeamStatsResponse stats)
    {
        double homeAttackShift = stats.Home.AvgGoalsScoredLast3 - stats.Home.AvgGoalsScoredLast7;
        double awayAttackShift = stats.Away.AvgGoalsScoredLast3 - stats.Away.AvgGoalsScoredLast7;

        double avgShift = (homeAttackShift + awayAttackShift) / 2.0;

        // Need at least 0.3 goals/game improvement to signal momentum
        if (avgShift < 0.3) return 0;

        // Scale: 0.3 goals → small boost, 1.0+ goals → max boost
        double boost = Math.Min(avgShift * 0.06, MaxBoost);
        return boost;
    }
}
