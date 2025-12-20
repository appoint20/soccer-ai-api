using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for fetching and managing European competition fixtures (UCL, Europa League)
/// </summary>
public interface IEuropeanFixturesService
{
    /// <summary>
    /// Fetch all European fixtures and save to disk
    /// </summary>
    Task<bool> UpdateEuropeanFixturesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get team's European fixtures from stored data
    /// </summary>
    Task<TeamEuropeanFixtures?> GetTeamFixturesAsync(string teamName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if team has recent European matches (last 14 days)
    /// </summary>
    Task<bool> HasRecentEuropeanMatchesAsync(string teamName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if team has upcoming European matches (next 60 days)
    /// </summary>
    Task<bool> HasUpcomingEuropeanMatchesAsync(string teamName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all teams currently in European competitions
    /// </summary>
    Task<List<string>> GetAllEuropeanTeamsAsync(CancellationToken cancellationToken = default);
}
