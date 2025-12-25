using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IEuropeanFatigueService
{
    /// <summary>
    /// Checks if a team has played in a European competition recently (within specified days)
    /// which may cause rotation and fatigue in their domestic matches.
    /// </summary>
    /// <param name="teamName">Name of the team to check</param>
    /// <param name="matchDate">Date of the upcoming domestic match</param>
    /// <param name="history">Historical match data to search through</param>
    /// <param name="lookbackDays">Number of days to look back (default: 7)</param>
    /// <returns>True if team has recent European fixture, false otherwise</returns>
    bool HasRecentEuropeanFixture(
        string teamName, 
        DateTime matchDate, 
        List<HistoricalMatchDto> history, 
        int lookbackDays = 7);
}
