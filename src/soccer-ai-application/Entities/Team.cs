namespace SoccerAi.Application.Entities;

/// <summary>
/// Represents a team's current standings in a league for a given season.
/// All timestamps are DateTimeOffset (UTC) for PostgreSQL compatibility.
/// </summary>
public class Team
{
    public int Id { get; set; }
    public int ApiId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public int LeagueId { get; set; }
    public int Rank { get; set; }
    public int Points { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalsDiff { get; set; }
    public int Played { get; set; }
    public int Win { get; set; }
    public int Lose { get; set; }
    public int Draw { get; set; }

    /// <summary>Recent form string (e.g., "WWLDW")</summary>
    public string Form { get; set; } = string.Empty;

    /// <summary>Relative strength metric (default 1500)</summary>
    public double Elo { get; set; } = 1500.0;

    /// <summary>Last time standings were synced from API</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
