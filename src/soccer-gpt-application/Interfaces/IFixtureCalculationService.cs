namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for calculating missing fixture values from historical data
/// </summary>
public interface IFixtureCalculationService
{
    /// <summary>
    /// Calculate rolling average of goals scored
    /// </summary>
    double CalculateGoalAverage(IEnumerable<int> recentGoals);

    /// <summary>
    /// Calculate rolling average of half-time goals
    /// </summary>
    double CalculateHtGoalAverage(IEnumerable<int?> recentHtGoals);

    /// <summary>
    /// Calculate average shots per match
    /// </summary>
    double CalculateShotsAverage(IEnumerable<int?> recentShots);
}
