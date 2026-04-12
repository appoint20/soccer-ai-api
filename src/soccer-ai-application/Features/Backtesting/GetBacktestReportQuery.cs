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
}

public sealed class BacktestSummary
{
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

    [JsonPropertyName("btts_accuracy")]
    public double BttsAccuracy { get; init; }

    [JsonPropertyName("over25_accuracy")]
    public double Over25Accuracy { get; init; }
}
