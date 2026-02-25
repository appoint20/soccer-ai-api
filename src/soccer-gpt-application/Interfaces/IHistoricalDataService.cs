using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for accessing historical match data from the persisted database
/// </summary>
public interface IHistoricalDataService
{
    /// <summary>
    /// Find a specific match in historical data
    /// </summary>
    Task<HistoricalMatchData?> FindMatchAsync(string homeTeam, string awayTeam, DateTime date, int leagueId);

    /// <summary>
    /// Get recent matches for a team (for calculating rolling averages)
    /// </summary>
    Task<List<HistoricalMatchData>> GetTeamHistoryAsync(string teamName, int leagueId, DateTime beforeDate, int limit = 6);

    /// <summary>
    /// Map API league ID to canonical division code
    /// </summary>
    string GetDivisionCode(int leagueId);

    /// <summary>
    /// Get list of available divisions with record counts
    /// </summary>
    Task<Dictionary<string, int>> GetAvailableDivisionsAsync();
}
