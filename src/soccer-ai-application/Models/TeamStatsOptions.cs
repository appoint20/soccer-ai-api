namespace SoccerAi.Application.Models;

public sealed class TeamStatsOptions
{
    public int LastMatches { get; init; } = 0;

    /// <summary>
    /// null = all matches  
    /// true = home matches only  
    /// false = away matches only
    /// </summary>
    public bool? HomeOnly { get; init; } = null;
}
