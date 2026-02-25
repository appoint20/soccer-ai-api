using System.ComponentModel.DataAnnotations;

namespace soccer_gpt_application.Entities;

/// <summary>
/// Fixture entity with enriched stats for analysis.
/// </summary>
public class Fixture
{
    [Key]
    public int Id { get; init; }

    /// <summary>API-Football fixture ID</summary>
    public int ApiId { get; init; }

    /// <summary>Home team API ID</summary>
    public int HomeTeamId { get; init; }

    /// <summary>Away team API ID</summary>
    public int AwayTeamId { get; init; }

    /// <summary>League API ID</summary>
    public int LeagueId { get; init; }

    /// <summary>Match date/time</summary>
    public DateTime Date { get; init; }

    /// <summary>Match status (NS=Not Started, FT=Full Time, etc.)</summary>
    public string Status { get; set; } = "NS";

    // ========== GOALS ==========

    /// <summary>Full-time home goals</summary>
    public int HomeGoal { get; set; }

    /// <summary>Full-time away goals</summary>
    public int AwayGoal { get; set; }

    /// <summary>Rolling average of home goals</summary>
    public double HomeGoalAvg { get; set; }

    /// <summary>Rolling average of away goals</summary>
    public double AwayGoalAvg { get; set; }

    // ========== HALF-TIME ==========

    /// <summary>Half-time home goals</summary>
    public int HtHomeGoal { get; set; }

    /// <summary>Half-time away goals</summary>
    public int HtAwayGoal { get; set; }

    /// <summary>Rolling average of HT home goals</summary>
    public double HtHomeGoalAvg { get; set; }

    /// <summary>Rolling average of HT away goals</summary>
    public double HtAwayGoalAvg { get; set; }

    // ========== SHOTS ==========

    /// <summary>Home team total shots</summary>
    public int HomeShots { get; set; }

    /// <summary>Away team total shots</summary>
    public int AwayShots { get; set; }

    /// <summary>Home team shots on target</summary>
    public int HomeShotsOnTarget { get; set; }

    /// <summary>Away team shots on target</summary>
    public int AwayShotsOnTarget { get; set; }

    // ========== POSSESSION/PASSES (nullable) ==========

    /// <summary>Home team ball possession percentage</summary>
    public int? HomeBallPossession { get; set; }

    /// <summary>Away team ball possession percentage</summary>
    public int? AwayBallPossession { get; set; }

    /// <summary>Home team accurate passes</summary>
    public int? HomePassesAccurate { get; set; }

    /// <summary>Away team accurate passes</summary>
    public int? AwayPassesAccurate { get; set; }

    // ========== xG ==========

    /// <summary>Home team expected goals</summary>
    public double HomeXg { get; set; }

    /// <summary>Away team expected goals</summary>
    public double AwayXg { get; set; }

    // ========== BETTING ODDS (nullable) ==========

    /// <summary>Home win odds (Bet365)</summary>
    public double? HomeWinOdds { get; set; }

    /// <summary>Draw odds (Bet365)</summary>
    public double? DrawOdds { get; set; }

    /// <summary>Away win odds (Bet365)</summary>
    public double? AwayWinOdds { get; set; }

    /// <summary>Over 2.5 goals odds</summary>
    public double? Over25Odds { get; set; }

    /// <summary>Under 2.5 goals odds</summary>
    public double? Under25Odds { get; set; }

    /// <summary>Both teams to score YES odds</summary>
    public double? BttsYesOdds { get; set; }

    // ========== FLAGS ==========

    /// <summary>True if from current season (used for Poisson calculations)</summary>
    public bool IsCurrentSeason { get; set; }

    /// <summary>True if this is a derby match</summary>
    public bool IsDerby { get; set; }

    /// <summary>Record creation timestamp</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update timestamp</summary>
    public DateTime? UpdatedAt { get; set; }

    // ========== GEMINI AI ANALYSIS ==========

    /// <summary>AI Final Recommendation from Gemini</summary>
    public string? GeminiRecommendation { get; set; }

    /// <summary>AI Confidence score from Gemini</summary>
    public double? GeminiConfidence { get; set; }

    /// <summary>AI reasoning text</summary>
    public string? GeminiReasoning { get; set; }

    /// <summary>General Match Analysis text from Gemini (Deep analysis)</summary>
    public string? GeminiAnalysis { get; set; }

    /// <summary>Whether Gemini flagged the match as a trap</summary>
    public bool? GeminiIsTrap { get; set; }

    /// <summary>Explicit reason why Gemini flagged the match as a trap, if applicable</summary>
    public string? GeminiTrapReason { get; set; }

    /// <summary>One-line user friendly prediction summary from Gemini</summary>
    public string? GeminiOneLineSummary { get; set; }

    public string? GeminiBttsSummary { get; set; }
    public string? GeminiOver25Summary { get; set; }
    public string? GeminiUnder25Summary { get; set; }
    public string? GeminiHomeWinSummary { get; set; }
    public string? GeminiAwayWinSummary { get; set; }
}
