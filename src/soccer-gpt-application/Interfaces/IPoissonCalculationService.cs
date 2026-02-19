using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for calculating Poisson probabilities with Dixon-Coles adjustment
/// </summary>
public interface IPoissonCalculationService
{
    /// <summary>
    /// Calculate match probabilities using Dixon-Coles Poisson model
    /// Uses historical matches from the same league before the fixture date
    /// </summary>
    /// <param name="leagueId">League API ID</param>
    /// <param name="homeTeamId">Home team API ID</param>
    /// <param name="awayTeamId">Away team API ID</param>
    /// <param name="matchDate">Match date (for historical cutoff)</param>
    /// <param name="ct">Cancellation token</param>
    Task<PoissonProbabilities?> CalculateProbabilitiesAsync(
        int leagueId, 
        int homeTeamId, 
        int awayTeamId, 
        DateTime matchDate,
        CancellationToken ct = default);
    
}


/// <summary>
/// League average statistics for Poisson calculations
/// </summary>
public record LeagueAverages(
    int LeagueId,
    double HomeGoalsAvg,
    double AwayGoalsAvg,
    int MatchesAnalyzed);
