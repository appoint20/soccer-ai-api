namespace soccer_gpt_application.Models;

/// <summary>
/// Represents a single historical match record used for calculations.
/// </summary>
public record HistoricalMatchData
{
    /// <summary>Date of the match</summary>
    public required DateTime Date { get; init; }

    /// <summary>Home team name</summary>
    public required string HomeTeam { get; init; }

    /// <summary>Away team name</summary>
    public required string AwayTeam { get; init; }

    /// <summary>Full-time home goals</summary>
    public required int Fthg { get; init; }

    /// <summary>Full-time away goals</summary>
    public required int Ftag { get; init; }

    /// <summary>Half-time home goals</summary>
    public int? Hthg { get; init; }

    /// <summary>Half-time away goals</summary>
    public int? Htag { get; init; }

    /// <summary>Home shots total</summary>
    public int? HomeShots { get; init; }

    /// <summary>Away shots total</summary>
    public int? AwayShots { get; init; }

    /// <summary>Home shots on target</summary>
    public int? HomeShotsOnTarget { get; init; }

    /// <summary>Away shots on target</summary>
    public int? AwayShotsOnTarget { get; init; }

    /// <summary>Division code (E0=PL, E1=Champ, E2=L1, E3=L2)</summary>
    public string Division { get; init; } = string.Empty;

    // ========== BETTING ODDS ==========
    
    /// <summary>Bet365 home win odds (Column: B365H)</summary>
    public double? HomeWinOdds { get; init; }

    /// <summary>Bet365 draw odds (Column: B365D)</summary>
    public double? DrawOdds { get; init; }

    /// <summary>Bet365 away win odds (Column: B365A)</summary>
    public double? AwayWinOdds { get; init; }

    /// <summary>Bet365 over 2.5 goals odds (Column: B365>2.5)</summary>
    public double? Over25Odds { get; init; }

    /// <summary>Bet365 under 2.5 goals odds (Column: B365&lt;2.5)</summary>
    public double? Under25Odds { get; init; }
}
