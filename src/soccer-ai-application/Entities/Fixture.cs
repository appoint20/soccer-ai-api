namespace SoccerAi.Application.Entities;

/// <summary>
/// Enriched fixture entity with stats, odds, and AI analysis.
/// All timestamps use DateTimeOffset (UTC) for PostgreSQL timezone safety.
/// </summary>
public class Fixture
{
    public int Id { get; set; }
    public int ApiId { get; set; }
    public int HomeTeamId { get; set; }
    public int AwayTeamId { get; set; }
    public int LeagueId { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = "NS";

    // ── Goals ──────────────────────────────────────────────────────────────
    public int HomeGoal { get; set; }
    public int AwayGoal { get; set; }
    public double HomeGoalAvg { get; set; }
    public double AwayGoalAvg { get; set; }

    // ── Half-time ──────────────────────────────────────────────────────────
    public int HtHomeGoal { get; set; }
    public int HtAwayGoal { get; set; }
    public double HtHomeGoalAvg { get; set; }
    public double HtAwayGoalAvg { get; set; }

    // ── Shots ──────────────────────────────────────────────────────────────
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public int HomeShotsOnTarget { get; set; }
    public int AwayShotsOnTarget { get; set; }

    // ── Possession / Passes (nullable — not always available) ──────────────
    public int? HomeBallPossession { get; set; }
    public int? AwayBallPossession { get; set; }
    public int? HomePassesAccurate { get; set; }
    public int? AwayPassesAccurate { get; set; }

    // ── Expected Goals ─────────────────────────────────────────────────────
    public double HomeXg { get; set; }
    public double AwayXg { get; set; }

    // ── Betting Odds (nullable — only fetched near match date) ────────────
    public double? HomeWinOdds { get; set; }
    public double? DrawOdds { get; set; }
    public double? AwayWinOdds { get; set; }
    public double? Over25Odds { get; set; }
    public double? Under25Odds { get; set; }
    public double? BttsYesOdds { get; set; }

    // ── ELO (Situational Context) ──────────────────────────────────────────
    public double? HomeElo { get; set; }
    public double? AwayElo { get; set; }

    // ── Flags ──────────────────────────────────────────────────────────────
    public bool IsCurrentSeason { get; set; }
    public bool IsDerby { get; set; }

    // ── Timestamps ─────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<FixtureAnalysis> Analyses { get; } = new List<FixtureAnalysis>();
}
