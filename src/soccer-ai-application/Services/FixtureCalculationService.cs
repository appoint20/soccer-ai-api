using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services;

/// <summary>
/// Service for calculating missing fixture values from historical data
/// </summary>
public class FixtureCalculationService : IFixtureCalculationService
{
    /// <summary>
    /// Calculate rolling average of goals scored
    /// </summary>
    public double CalculateGoalAverage(IEnumerable<int> recentGoals)
    {
        var goalsList = recentGoals.ToList();
        if (goalsList.Count == 0)
            return 0.0;

        return Math.Round(goalsList.Average(), 2);
    }

    /// <summary>
    /// Calculate rolling average of half-time goals (ignores nulls)
    /// </summary>
    public double CalculateHtGoalAverage(IEnumerable<int?> recentHtGoals)
    {
        var validGoals = recentHtGoals.Where(g => g.HasValue).Select(g => g!.Value).ToList();
        if (validGoals.Count == 0)
            return 0.0;

        return Math.Round(validGoals.Average(), 2);
    }

    /// <summary>
    /// Calculate average shots per match (ignores nulls)
    /// </summary>
    public double CalculateShotsAverage(IEnumerable<int?> recentShots)
    {
        var validShots = recentShots.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        if (validShots.Count == 0)
            return 0.0;

        return Math.Round(validShots.Average(), 2);
    }
}
