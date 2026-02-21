using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Professional weighted scoring system for match qualification.
/// Replaces rigid boolean filters with a 0-100 progressive score.
/// </summary>
public sealed class FeatureScoringEngine(IExpectedValueEngine evEngine) : IFeatureScoringEngine
{
    public double CalculateGoalScore(
        double modelProbability, 
        TeamStatsResponse teamStats, 
        double marketOdds)
    {
        double score = 0;

        // 1. Model Probability Weight (40%)
        // We scale the probability linearly. (e.g. 60% probability = 24 points)
        score += modelProbability * 40.0;

        // 2. Team Scoring Rate (20%)
        // High recent scoring form confirms model
        var avgScored = (teamStats.Home.AvgGoalsScoredLast7 + teamStats.Away.AvgGoalsScoredLast7) / 2.0;
        // Cap at 2.5 average scored for max points (20)
        score += Math.Min(avgScored / 2.5, 1.0) * 20.0;

        // 3. Conceding Rate (20%)
        // High recent conceding form confirms defensive fragility required for goals
        var avgConceded = (teamStats.Home.AvgGoalsConcededLast7 + teamStats.Away.AvgGoalsConcededLast7) / 2.0;
        // Cap at 2.0 average conceded for max points (20)
        score += Math.Min(avgConceded / 2.0, 1.0) * 20.0;

        // 4. Market Agreement / EV (20%)
        // If the market agrees or if there's massive value, award the final 20 points
        if (marketOdds > 1.0) 
        {
            double ev = evEngine.CalculateEV(modelProbability, marketOdds);
            
            // Expected Value scoring:
            // > 5% EV = 20 pts (Max)
            // > 2% EV = 15 pts
            // > 0% EV = 10 pts
            // Negative EV = 0 pts
            if (ev >= 0.05) score += 20.0;
            else if (ev >= 0.02) score += 15.0;
            else if (ev >= 0.0) score += 10.0;
        }
        else
        {
            // If odds aren't available, we assume neutral market agreement (10/20 pts)
            score += 10.0;
        }

        // Cap at 100
        return Math.Round(Math.Min(score, 100.0), 2);
    }
}
