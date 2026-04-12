using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

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
        score += modelProbability * 40.0;

        // 2. Team Scoring Rate (20%)
        var avgScored = (teamStats.Home.AvgGoalsScoredLast7 + teamStats.Away.AvgGoalsScoredLast7) / 2.0;
        score += Math.Min(avgScored / 2.5, 1.0) * 20.0;

        // 3. Conceding Rate (20%)
        var avgConceded = (teamStats.Home.AvgGoalsConcededLast7 + teamStats.Away.AvgGoalsConcededLast7) / 2.0;
        score += Math.Min(avgConceded / 2.0, 1.0) * 20.0;

        // 4. Market Agreement / EV (20%)
        if (marketOdds > 1.0) 
        {
            double ev = evEngine.CalculateEV(modelProbability, marketOdds);
            if (ev >= 0.05) score += 20.0;
            else if (ev >= 0.02) score += 15.0;
            else if (ev >= 0.0) score += 10.0;
        }
        else score += 10.0;

        return Math.Round(Math.Min(score, 100.0), 2);
    }

    public double CalculateGoals23Score(
        double modelProbability,
        TeamStatsResponse teamStats,
        double fixedOdds = 1.90)
    {
        double score = 0;

        // 1. Model Probability (40%)
        score += modelProbability * 40.0;

        // 2. Controlled Range Consistency (30%)
        // Matches avec une moyenne totale entre 2.0 et 3.0 sont idéaux pour le 2-3 goals.
        var avgTotal = (teamStats.Home.AvgGoalsScoredLast7 + teamStats.Home.AvgGoalsConcededLast7 + 
                        teamStats.Away.AvgGoalsScoredLast7 + teamStats.Away.AvgGoalsConcededLast7) / 2.0;
        
        if (avgTotal >= 2.0 && avgTotal <= 3.0) score += 30.0;
        else if (avgTotal >= 1.5 && avgTotal <= 3.5) score += 15.0;

        // 3. EV against 1.90 fixed (30%)
        double ev = evEngine.CalculateEV(modelProbability, fixedOdds);
        if (ev >= 0.10) score += 30.0;
        else if (ev >= 0.05) score += 20.0;
        else if (ev >= 0.0) score += 10.0;

        return Math.Round(Math.Min(score, 100.0), 2);
    }
}
