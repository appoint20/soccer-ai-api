namespace SoccerAi.Application.Interfaces;

public interface ILeagueAdjustmentService
{
    /// <summary>
    /// Returns the points to detract/add from the threshold requirement based on the league's scoring environment.
    /// Lower scoring leagues get negative modifiers to allow more matches to qualify.
    /// Higher scoring leagues get zero modifiers (or positive to harden requirements).
    /// </summary>
    double GetGoalThresholdModifier(string leagueName);
}
