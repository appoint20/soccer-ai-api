namespace SoccerAi.Infrastructure.MlNet.Models;

/// <summary>
/// One training row per fixture-market, built STRICTLY from data available
/// before the fixture date. Metadata columns are excluded from training via
/// AutoML ColumnInformation.
/// </summary>
public sealed class MarketTrainingRow
{
    // ── Metadata (never used as features) ───────────────────────────────────
    public float FixtureId { get; set; }
    public float LeagueId { get; set; }
    public DateTime Date { get; set; }
    public string Market { get; set; } = "";

    // ── Features ─────────────────────────────────────────────────────────────
    /// <summary>Dixon-Coles probability for this market outcome.</summary>
    public float DcProb { get; set; }

    /// <summary>Shin-margin-removed market probability (0.5 when no odds).</summary>
    public float MarketProb { get; set; }

    /// <summary>1 when real odds backed MarketProb, 0 when it is the neutral fallback.</summary>
    public float HasMarketProb { get; set; }

    /// <summary>DcProb − MarketProb (model vs market divergence).</summary>
    public float DcMarketDelta { get; set; }

    public float EloDiff { get; set; }
    public float HomeRestDays { get; set; }
    public float AwayRestDays { get; set; }
    public float RestDaysDiff { get; set; }

    /// <summary>Points share over the last 5 matches (0..1).</summary>
    public float HomeForm { get; set; }
    public float AwayForm { get; set; }
    public float FormDiff { get; set; }

    public float LeagueVolatility { get; set; }

    // ── Label ────────────────────────────────────────────────────────────────
    public bool Label { get; set; }

    public static class Markets
    {
        public const string Over25 = "over25";
        public const string Btts = "btts";
        public const string Goals23 = "goals23";
        public const string HomeWin = "home_win";
        public const string AwayWin = "away_win";

        public static readonly string[] All = [Over25, Btts, Goals23, HomeWin, AwayWin];
    }

    /// <summary>Columns AutoML must ignore (metadata, not signal).</summary>
    public static readonly string[] MetadataColumns =
        [nameof(FixtureId), nameof(LeagueId), nameof(Date), nameof(Market)];
}
