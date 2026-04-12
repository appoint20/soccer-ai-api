using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface IFeatureScoringEngine
{
    /// <summary>
    /// Calculates a weighted 0-100 score for a Goal Market prediction (BTTS or Over 2.5).
    /// </summary>
    double CalculateGoalScore(
        double modelProbability, 
        TeamStatsResponse teamStats, 
        double marketOdds);

    /// <summary>
    /// Calculates a weighted 0-100 score for the 2-3 Goals market.
    /// Focuses on scoring consistency and controlled goal range probability.
    /// </summary>
    double CalculateGoals23Score(
        double modelProbability,
        TeamStatsResponse teamStats,
        double fixedOdds = 1.90);
}
