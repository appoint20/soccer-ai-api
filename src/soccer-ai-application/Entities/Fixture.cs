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
    public double? HomeObservedXg { get; set; }
    public double? AwayObservedXg { get; set; }
    public DateTimeOffset? StatisticsUpdatedAtUtc { get; set; }

    // ── Betting Odds (nullable — only fetched near match date) ────────────
    public double? HomeWinOdds { get; set; }
    public double? DrawOdds { get; set; }
    public double? AwayWinOdds { get; set; }
    public double? Over25Odds { get; set; }
    public double? Under25Odds { get; set; }
    public double? BttsYesOdds { get; set; }
    public double? Goals23Odds { get; set; }
    public double? BttsAndOver25Odds { get; set; }
    /// <summary>Bookmaker supplying current live columns; null on legacy/mixed-source rows.</summary>
    public string? OddsBookmaker { get; set; }

    /// <summary>Last odds request; independent of whether the price moved.</summary>
    public DateTimeOffset? OddsCheckedAtUtc { get; set; }

    /// <summary>
    /// When reported absences were last fetched for this fixture. Provenance,
    /// exactly as for odds: a report read after kickoff is a confirmation, not a
    /// prediction input.
    /// </summary>
    public DateTimeOffset? InjuriesCheckedAtUtc { get; set; }

    /// <summary>
    /// When this pairing's past meetings were last fetched from the provider.
    /// </summary>
    /// <remarks>
    /// Marked whether or not meetings came back, so a pair that has genuinely
    /// never played is asked about once rather than on every sync. Finished
    /// matches do not change, so this is never re-checked for the same fixture.
    /// </remarks>
    public DateTimeOffset? HeadToHeadCheckedAtUtc { get; set; }

    /// <summary>
    /// When the provider's prediction for this fixture was last fetched.
    /// </summary>
    /// <remarks>
    /// Fetched once when the fixture appears and refreshed once inside the last
    /// day, when the form it is built on has stopped moving. Two requests over
    /// a fixture's life, against a budget already running near its ceiling.
    /// </remarks>
    public DateTimeOffset? PredictionCheckedAtUtc { get; set; }

    /// <summary>
    /// Minutes played, while the match is in play. Null before kickoff and
    /// after the final whistle — a finished match is described by its score,
    /// not by the minute it ended on.
    /// </summary>
    public int? ElapsedMinutes { get; set; }

    /// <summary>Added time in the current period, when the provider reports it.</summary>
    public int? ExtraMinutes { get; set; }

    /// <summary>When the live score was last refreshed.</summary>
    public DateTimeOffset? LiveCheckedAtUtc { get; set; }
    /// <summary>Oldest provider update among the currently usable prices.</summary>
    public DateTimeOffset? OddsUpdatedAtUtc { get; set; }

    // ── ELO (Situational Context) ──────────────────────────────────────────
    public double? HomeElo { get; set; }
    public double? AwayElo { get; set; }

    // ── Flags ──────────────────────────────────────────────────────────────
    public bool IsCurrentSeason { get; set; }
    public bool IsDerby { get; set; }

    // ── Timestamps ─────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // ── Contextual Intelligence Data ──
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }

    public ICollection<FixtureAnalysis> Analyses { get; } = new List<FixtureAnalysis>();
}
