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
}

public sealed class MarketCalibration
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = "";

    [JsonPropertyName("buckets")]
    public List<CalibrationBucketRow> Buckets { get; init; } = [];
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
