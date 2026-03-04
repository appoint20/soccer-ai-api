namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Adjusts probabilities based on league predictability.
/// High-variance leagues (lower divisions) → shrink toward 0.5.
/// Stable leagues (top 5) → trust model more.
/// </summary>
public interface ILeagueVolatilityService
{
    double AdjustProbability(int leagueId, double probability);
    double GetVolatility(int leagueId);
}
