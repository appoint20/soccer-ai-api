using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// The single statistical probability source for all markets.
/// Dixon-Coles adjusted Poisson model; every market probability comes
/// from the same renormalized score matrix.
/// </summary>
public interface IDixonColesModel
{
    /// <summary>
    /// Calculate match probabilities from historical league matches strictly
    /// before <paramref name="matchDate"/>. Returns null when there is not
    /// enough data for a reliable estimate.
    /// </summary>
    Task<PoissonProbabilities?> CalculateProbabilitiesAsync(
        int leagueId,
        int homeTeamId,
        int awayTeamId,
        DateTimeOffset matchDate,
        CancellationToken ct = default);
}

/// <summary>Time-decay-weighted league scoring averages.</summary>
public sealed record LeagueAverages(
    int LeagueId,
    double HomeGoalsAvg,
    double AwayGoalsAvg,
    int MatchesAnalyzed);
