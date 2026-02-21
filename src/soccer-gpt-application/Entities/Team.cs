using System.ComponentModel.DataAnnotations;

namespace soccer_gpt_application.Entities;

public class Team
{
    [Key]
    public int Id { get; init; }

    public int ApiId { get; init; }

    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    public int LeagueId { get; set; }

    public int Rank { get; set; }

    /// <summary>Total points accumulated</summary>
    public int Points { get; set; }

    /// <summary>Total goals scored</summary>
    public int GoalsFor { get; set; }

    /// <summary>Total goals conceded</summary>
    public int GoalsAgainst { get; set; }

    /// <summary>Goal difference (GoalsFor - GoalsAgainst)</summary>
    public int GoalsDiff { get; set; }

    /// <summary>Number of matches played</summary>
    public int Played { get; set; }

    /// <summary>Number of wins</summary>
    public int Win { get; set; }

    /// <summary>Number of losses</summary>
    public int Lose { get; set; }

    /// <summary>Number of draws</summary>
    public int Draw { get; set; }

    /// <summary>Recent form string (e.g., "WWLDW")</summary>
    [MaxLength(10)]
    public string Form { get; set; } = string.Empty;

    /// <summary>Last time standings were updated</summary>
    public DateTime UpdatedAt { get; set; }
}

