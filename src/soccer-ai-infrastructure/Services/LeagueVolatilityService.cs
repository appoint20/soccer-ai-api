using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Shrinks model probabilities toward 0.5 based on league volatility.
/// Lower divisions have higher variance → less trustworthy predictions.
/// Volatility values calibrated from historical season data.
/// </summary>
public sealed class LeagueVolatilityService : ILeagueVolatilityService
{
    private const double DefaultVolatility = 0.15;

    // Precomputed from historical seasons — lower = more predictable
    private static readonly Dictionary<int, double> Volatility = new()
    {
        // Top 5 leagues — most predictable
        { 39, 0.08 },   // Premier League
        { 78, 0.08 },   // Bundesliga
        { 140, 0.10 },  // La Liga
        { 135, 0.10 },  // Serie A
        { 61, 0.10 },   // Ligue 1

        // Second divisions — medium volatility
        { 40, 0.20 },   // Championship
        { 79, 0.15 },   // 2. Bundesliga
        { 141, 0.18 },  // La Liga 2
        { 136, 0.18 },  // Serie B
        { 62, 0.18 },   // Ligue 2

        // Lower divisions — high volatility
        { 41, 0.25 },   // League One
        { 42, 0.30 },   // League Two
    };

    public double AdjustProbability(int leagueId, double probability)
    {
        var v = GetVolatility(leagueId);
        // Shrink toward neutral 0.5
        return probability * (1 - v) + 0.5 * v;
    }

    public double GetVolatility(int leagueId)
        => Volatility.GetValueOrDefault(leagueId, DefaultVolatility);
}
