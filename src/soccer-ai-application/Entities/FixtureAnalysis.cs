namespace SoccerAi.Application.Entities;

/// <summary>
/// Stores the persisted AI analysis result for a fixture.
/// One row per fixture per language (e.g. "en", "de").
/// </summary>
public class FixtureAnalysis
{
    public int Id { get; set; }
    public int FixtureId { get; set; }

    /// <summary>Language code: "en" or "de"</summary>
    public string Lang { get; set; } = "en";

    // ── Core result ────────────────────────────────────────────────
    public string Recommendation { get; set; } = "";
    public double Confidence { get; set; }

    // ── Mathematical Cache (for backtest optimization) ───────────
    public double HomeProb { get; set; }
    public double DrawProb { get; set; }
    public double AwayProb { get; set; }
    public double Over25Prob { get; set; }
    public double BttsProb { get; set; }
    public double Goals23Prob { get; set; }

    /// <summary>2-4 key factors behind the recommendation</summary>
    public string PredictionReason { get; set; } = "";

    /// <summary>6-8 sentence match analysis</summary>
    public string Analysis { get; set; } = "";

    // ── Trap detection ─────────────────────────────────────────────
    public bool TrapDetected { get; set; }
    public string? TrapReason { get; set; }

    /// <summary>One sentence explaining model predictions agreement</summary>
    public string ConsensusEvaluation { get; set; } = "";

    // ── Market summaries ───────────────────────────────────────────
    public string? BttsSummary { get; set; }
    public string? Over25Summary { get; set; }
    public string? Under25Summary { get; set; }
    public string? HomeWinSummary { get; set; }
    public string? AwayWinSummary { get; set; }

    // ── AI Decision Layer (per-market qualifications) ────────────
    public bool AiOver25Qualified { get; set; }
    public bool AiBttsQualified { get; set; }
    public bool AiUnder25Qualified { get; set; }
    public bool AiGoals23Qualified { get; set; }
    public bool AiHomeWinQualified { get; set; }
    public bool AiAwayWinQualified { get; set; }
    public string AiBestBet { get; set; } = "";
    public int AiOverallConfidence { get; set; }

    // ── Precomputed response snapshot (Task 1.5) ──────────────────
    /// <summary>
    /// Full serialized MatchAnalysis response for this fixture+language,
    /// computed during sync so GET /api/analyze is a pure DB read.
    /// </summary>
    public string? SnapshotJson { get; set; }

    // ── Audit ──────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
