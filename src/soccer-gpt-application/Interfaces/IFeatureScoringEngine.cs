using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IFeatureScoringEngine
{
    /// <summary>
    /// Calculates a weighted 0-100 score for a Goal Market prediction (BTTS or Over 2.5).
    /// </summary>
    double CalculateGoalScore(
        double modelProbability, 
        TeamStatsResponse teamStats, 
        double marketOdds);
}
