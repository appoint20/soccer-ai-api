using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

/// <summary>
/// Match context - immutable facts about the fixture
/// </summary>
/// <summary>
/// Match context - immutable facts about the fixture
/// </summary>
public sealed class MatchContext
{
    public DateTime Date { get; init; }
    public TimeSpan Time { get; init; }
    public string LeagueName { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public MatchResult? Result { get; init; }
    
    public double OddsHome { get; init; }
    public double OddsDraw { get; init; }
    public double OddsAway { get; init; }
    public double OddsOver25 { get; init; }
    public double OddsBttsYes { get; init; }
}

public sealed class MatchResult
{
    public bool IsCorrect { get; init; }
    public string ActualScore { get; init; } = string.Empty;
}

public sealed class TeamStats
{
    // ---------- TEAM INFO ----------
    public int Rank { get; set; }
    public int Points { get; set; }
    public string Form { get; set; } = "";
    public int FormPercentage { get; set; }

    // ---------- LAST 3 (HOME OR AWAY ONLY) ----------
    public double AvgGoalsScoredLast3 { get; set; }
    public double AvgGoalsConcededLast3 { get; set; }
    public double BTTSRateLast3 { get; set; }
    public double Over25RateLast3 { get; set; }

    // ---------- LAST 7 OVERALL ----------
    public double AvgGoalsScoredLast7 { get; set; }
    public double AvgGoalsConcededLast7 { get; set; }
    public double BTTSRateLast7 { get; set; }
    public double Over25RateLast7 { get; set; }

    // ---------- PERFORMANCE ----------
    public double AttackStrength { get; set; }
    public double DefensiveStrength { get; set; }

    // ---------- RESULTS ----------
    public double CleanSheetRate { get; set; } // opponent scored 0
    public double ZeroZeroRate { get; set; }
    public double WinRate { get; set; }
    public double DrawRate { get; set; }
    
    public static TeamStats Empty => new();
}

/// <summary>
/// Final weighted stats for both teams
/// </summary>
public sealed class TeamStatsResponse
{
    public TeamStats Home { get; init; } = TeamStats.Empty;
    public TeamStats Away { get; init; } = TeamStats.Empty;
}
