using Mediator.Net.Contracts;
using System.Text.Json.Serialization;

namespace SoccerAi.Application.Features.Backtesting;

public record GetBacktestReportQuery(int WeeksBack = 10, double Stake = 100.0, bool Refresh = false) : IRequest;

public sealed class GetBacktestReportResponse : IResponse
{
    [JsonPropertyName("summary")]
    public BacktestSummary Summary { get; init; } = new();

    [JsonPropertyName("weekly_breakdown")]
    public List<WeeklyBreakdown> WeeklyBreakdown { get; init; } = [];

    [JsonPropertyName("league_accuracy")]
    public List<LeagueAccuracy> LeagueAccuracy { get; init; } = [];

    /// <summary>Per-market probabilistic quality (BTTS, Over25, 2-3 Goals, 1X2).</summary>
    [JsonPropertyName("market_metrics")]
    public List<MarketMetrics> MarketMetrics { get; init; } = [];

    /// <summary>Per-market calibration buckets (50-55, 55-60, 60-65, 65+).</summary>
    [JsonPropertyName("calibration")]
    public List<MarketCalibration> Calibration { get; init; } = [];

    /// <summary>Headline product metric: qualified picks only.</summary>
    [JsonPropertyName("qualified_picks")]
    public QualifiedPicksReport QualifiedPicks { get; init; } = new();

    /// <summary>Per-rule performance among qualified picks (with vs without).</summary>
    [JsonPropertyName("rule_performance")]
    public List<RulePerformanceRow> RulePerformance { get; init; } = [];

    /// <summary>Value-gate funnel per market (incl. filtered-by-MinOdds counts).</summary>
    [JsonPropertyName("qualification_funnel")]
    public List<QualificationFunnelRow> QualificationFunnel { get; init; } = [];

    /// <summary>Would-be performance of picks the price gates rejected (measurement only).</summary>
    [JsonPropertyName("shadow_cohorts")]
    public List<ShadowCohortRow> ShadowCohorts { get; init; } = [];

    /// <summary>Avg |model − market| divergence per league — where edge can exist.</summary>
    [JsonPropertyName("league_divergence")]
    public List<LeagueDivergenceRow> LeagueDivergence { get; init; } = [];
}

public sealed class LeagueDivergenceRow
{
    [JsonPropertyName("league")]
    public string League { get; init; } = "";

    [JsonPropertyName("n")]
    public int SampleSize { get; init; }

    /// <summary>Mean |p_model − p_market| across all sampled markets.</summary>
    [JsonPropertyName("avg_divergence")]
    public double AvgDivergence { get; init; }

    [JsonPropertyName("over25")]
    public double Over25 { get; init; }

    [JsonPropertyName("btts")]
    public double Btts { get; init; }

    [JsonPropertyName("match_winner")]
    public double MatchWinner { get; init; }
}

public sealed class ShadowCohortRow
{
    [JsonPropertyName("cohort")]
    public string Cohort { get; init; } = "";

    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    /// <summary>"ALL" = cohort×market total; otherwise the league name.</summary>
    [JsonPropertyName("league")]
    public string League { get; init; } = "";

    [JsonPropertyName("n")]
    public int Count { get; init; }

    [JsonPropertyName("hits")]
    public int Hits { get; init; }

    [JsonPropertyName("hit_rate")]
    public double HitRate { get; init; }

    [JsonPropertyName("avg_odds")]
    public double AvgOdds { get; init; }

    [JsonPropertyName("avg_ev")]
    public double AvgEv { get; init; }

    /// <summary>Flat 1-unit ROI these picks WOULD have produced.</summary>
    [JsonPropertyName("would_be_roi_percent")]
    public double WouldBeRoiPercent { get; init; }
}

public sealed class RulePerformanceRow
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = "";

    [JsonPropertyName("picks_with")]
    public int PicksWith { get; init; }

    [JsonPropertyName("hit_rate_with")]
    public double HitRateWith { get; init; }

    [JsonPropertyName("picks_without")]
    public int PicksWithout { get; init; }

    [JsonPropertyName("hit_rate_without")]
    public double HitRateWithout { get; init; }
}

public sealed class MarketMetrics
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    [JsonPropertyName("n")]
    public int SampleSize { get; init; }

    [JsonPropertyName("brier_score")]
    public double BrierScore { get; init; }

    [JsonPropertyName("log_loss")]
    public double LogLoss { get; init; }

    /// <summary>Share of analyzed fixtures whose stored odds pass the 1.01-15.0 sanity guard.</summary>
    [JsonPropertyName("valid_odds_pct")]
    public double ValidOddsPct { get; init; }
}

public sealed class MarketCalibration
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    /// <summary>Post-isotonic (what the EV gate consumed).</summary>
    [JsonPropertyName("buckets")]
    public List<CalibrationBucketRow> Buckets { get; init; } = [];

    /// <summary>Pre-isotonic — side-by-side comparison per acceptance criteria.</summary>
    [JsonPropertyName("raw_buckets")]
    public List<CalibrationBucketRow> RawBuckets { get; init; } = [];
}

public sealed class CalibrationBucketRow
{
    [JsonPropertyName("range")]
    public string Range { get; init; } = "";

    [JsonPropertyName("n")]
    public int SampleSize { get; init; }

    [JsonPropertyName("predicted_avg")]
    public double PredictedAvg { get; init; }

    [JsonPropertyName("actual_hit_rate")]
    public double ActualHitRate { get; init; }
}

public sealed class QualifiedPicksReport
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("hits")]
    public int Hits { get; init; }

    [JsonPropertyName("hit_rate")]
    public double HitRate { get; init; }

    [JsonPropertyName("avg_odds")]
    public double AvgOdds { get; init; }

    [JsonPropertyName("total_staked")]
    public double TotalStaked { get; init; }

    [JsonPropertyName("total_returned")]
    public double TotalReturned { get; init; }

    /// <summary>ROI at real odds, percent. Picks without stored odds are excluded from ROI.</summary>
    [JsonPropertyName("roi_percent")]
    public double RoiPercent { get; init; }

    /// <summary>ROI when staking fractional (quarter) Kelly instead of flat.</summary>
    [JsonPropertyName("kelly_roi_percent")]
    public double KellyRoiPercent { get; init; }

    /// <summary>Average EV (p×odds − 1) across picks with valid odds.</summary>
    [JsonPropertyName("avg_ev")]
    public double AvgEv { get; init; }

    [JsonPropertyName("per_market")]
    public List<QualifiedMarketRow> PerMarket { get; init; } = [];
}

public sealed class QualifiedMarketRow
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("hits")]
    public int Hits { get; init; }

    [JsonPropertyName("hit_rate")]
    public double HitRate { get; init; }

    [JsonPropertyName("avg_odds")]
    public double AvgOdds { get; init; }

    [JsonPropertyName("roi_percent")]
    public double RoiPercent { get; init; }

    /// <summary>The probability threshold that gated qualification for this market.</summary>
    [JsonPropertyName("qualification_threshold")]
    public double QualificationThreshold { get; init; }

    /// <summary>Share of these picks with guard-valid odds (only they enter ROI).</summary>
    [JsonPropertyName("valid_odds_pct")]
    public double ValidOddsPct { get; init; }

    [JsonPropertyName("kelly_roi_percent")]
    public double KellyRoiPercent { get; init; }

    [JsonPropertyName("avg_ev")]
    public double AvgEv { get; init; }
}

/// <summary>Value-gate funnel: where fixtures dropped out per market/league.</summary>
public sealed class QualificationFunnelRow
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    /// <summary>"ALL" = all leagues; "all" market = league aggregate.</summary>
    [JsonPropertyName("league")]
    public string League { get; init; } = "ALL";

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("analysis_only_no_odds")]
    public int AnalysisOnlyNoOdds { get; init; }

    /// <summary>+EV-or-not picks rejected purely by the MinOdds floor.</summary>
    [JsonPropertyName("below_min_odds")]
    public int BelowMinOdds { get; init; }

    [JsonPropertyName("below_min_edge")]
    public int BelowMinEdge { get; init; }

    [JsonPropertyName("below_probability_floor")]
    public int BelowProbabilityFloor { get; init; }

    [JsonPropertyName("vetoed")]
    public int Vetoed { get; init; }

    [JsonPropertyName("insufficient_confirms")]
    public int InsufficientConfirms { get; init; }

    [JsonPropertyName("qualified")]
    public int Qualified { get; init; }
}

public sealed class BacktestSummary
{
    [JsonPropertyName("start_date")]
    public DateTimeOffset StartDate { get; init; }

    [JsonPropertyName("total_roi")]
    public double TotalRoi { get; init; }

    [JsonPropertyName("total_staked")]
    public double TotalStaked { get; init; }

    [JsonPropertyName("total_returned")]
    public double TotalReturned { get; init; }

    [JsonPropertyName("combination_accuracy")]
    public double CombinationAccuracy { get; init; }

    [JsonPropertyName("win_rate")]
    public double WinRate { get; init; }

    [JsonPropertyName("combos_total")]
    public int CombosTotal { get; init; }

    [JsonPropertyName("combos_won")]
    public int CombosWon { get; init; }

    [JsonPropertyName("match_analysis_accuracy")]
    public double MatchAnalysisAccuracy { get; init; }

    [JsonPropertyName("total_legs")]
    public int TotalLegs { get; init; }

    [JsonPropertyName("correct_legs")]
    public int CorrectLegs { get; init; }
}

public sealed class WeeklyBreakdown
{
    [JsonPropertyName("week")]
    public string Week { get; init; } = "";

    [JsonPropertyName("date_range")]
    public string DateRange { get; init; } = "";

    [JsonPropertyName("total_combinations")]
    public int TotalCombinations { get; init; }

    [JsonPropertyName("combinations_won")]
    public int CombinationsWon { get; init; }

    [JsonPropertyName("stake_amount")]
    public double StakeAmount { get; init; }

    [JsonPropertyName("profit_loss")]
    public double ProfitLoss { get; init; }

    [JsonPropertyName("roi_percent")]
    public double RoiPercent { get; init; }
}

public sealed class LeagueAccuracy
{
    [JsonPropertyName("league")]
    public string League { get; init; } = "";

    [JsonPropertyName("n")]
    public int SampleSize { get; init; }

    /// <summary>True when n &lt; 30 — treat accuracies as anecdotal.</summary>
    [JsonPropertyName("low_sample")]
    public bool LowSample { get; init; }

    [JsonPropertyName("btts_accuracy")]
    public double BttsAccuracy { get; init; }

    [JsonPropertyName("over25_accuracy")]
    public double Over25Accuracy { get; init; }
}
