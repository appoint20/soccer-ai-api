using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for analyzing matches and providing comprehensive statistics and predictions
/// Centralizes historical data reading, team stats extraction, and Poisson calculations
/// </summary>
public interface IAnalyseService
{
    /// <summary>
    /// Analyze a single match and return comprehensive statistics and predictions
    /// </summary>
    /// <param name="homeTeam">Home team name</param>
    /// <param name="awayTeam">Away team name</param>
    /// <param name="league">League code (optional)</param>
    /// <param name="odds">Match odds (optional)</param>
    /// <returns>Complete match analysis with stats, probabilities, and betting decision</returns>
    Task<MatchAnalysisDto> AnalyzeMatchAsync(
        string homeTeam,
        string awayTeam,
        string? league = null,
        MatchOddsDto? odds = null);
    
    /// <summary>
    /// Analyze multiple matches in batch (for upcoming matches or ticket generation)
    /// </summary>
    /// <param name="fixtures">List of match fixtures to analyze</param>
    /// <returns>List of match analyses</returns>
    Task<List<MatchAnalysisDto>> AnalyzeMatchesAsync(List<MatchFixtureDto> fixtures);
}
