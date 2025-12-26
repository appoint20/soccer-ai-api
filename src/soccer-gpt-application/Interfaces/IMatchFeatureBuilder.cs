using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Feature engineering service for match analysis
/// Extracts proper venue-specific, time-weighted, and opponent-adjusted features
/// </summary>
public interface IMatchFeatureBuilder
{
    /// <summary>
    /// Build comprehensive feature set for a match
    /// </summary>
    /// <param name="homeTeam">Home team name</param>
    /// <param name="awayTeam">Away team name</param>
    /// <param name="league">League code</param>
    /// <param name="matchDate">Optional match date for time-based filtering</param>
    /// <returns>Complete feature set</returns>
    Task<MatchFeaturesDto> BuildFeaturesAsync(
        string homeTeam,
        string awayTeam,
        string league,
        DateTime? matchDate = null);
    
    /// <summary>
    /// Build features for multiple matches (batch with league normalization)
    /// </summary>
    Task<List<MatchFeaturesDto>> BuildFeaturesBatchAsync(
        List<(string HomeTeam, string AwayTeam, string League)> fixtures);
    
    /// <summary>
    /// Get league context for normalization
    /// </summary>
    Task<LeagueContext> GetLeagueContextAsync(string league);
}
