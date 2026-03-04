using Mediator.Net.Contracts;
using System.Text.Json.Serialization;

namespace SoccerAi.Application.Features.Backtesting;

public record GetBacktestReportQuery(int WeeksBack = 10, double Stake = 25.0) : IRequest;

public sealed class GetBacktestReportResponse : IResponse
{
    [JsonPropertyName("summary")]
    public BacktestSummary Summary { get; init; } = new();

    [JsonPropertyName("markets")]
    public List<MarketAccuracy> Markets { get; init; } = [];

    [JsonPropertyName("leagues")]
    public List<LeagueAccuracy> Leagues { get; init; } = [];

    [JsonPropertyName("league_markets")]
    public List<LeagueMarketAccuracy> LeagueMarkets { get; init; } = [];

    [JsonPropertyName("matches")]
    public List<MatchBacktestDetail> Matches { get; init; } = [];

    [JsonPropertyName("trap_stats")]
    public AccuracyStats TrapStats { get; init; } = new();

    [JsonPropertyName("gemini_stats")]
    public AccuracyStats GeminiStats { get; init; } = new();
}

public sealed class AccuracyStats
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    [JsonPropertyName("correct_count")]
    public int CorrectCount { get; init; }

    [JsonPropertyName("accuracy_percent")]
    public double AccuracyPercent => TotalCount > 0 ? Math.Round((double)CorrectCount / TotalCount * 100, 1) : 0;
}

public sealed class MatchBacktestDetail
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }

    [JsonPropertyName("league")]
    public string League { get; init; } = "";

    [JsonPropertyName("match")]
    public string MatchName { get; init; } = "";

    [JsonPropertyName("score")]
    public string Score { get; init; } = "";

    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = "";

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; init; }

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "";

    [JsonPropertyName("is_trap")]
    public bool IsTrap { get; init; }

    [JsonPropertyName("trap_reason")]
    public string TrapReason { get; init; } = "";

    [JsonPropertyName("gemini_recommendation")]
    public string GeminiRecommendation { get; init; } = "";

    [JsonPropertyName("gemini_is_trap")]
    public bool GeminiIsTrap { get; init; }
}

public sealed class BacktestSummary
{
    [JsonPropertyName("combos_total")]
    public int CombosTotal { get; init; }
    
    [JsonPropertyName("combos_won")]
    public int CombosWon { get; init; }

    [JsonPropertyName("total_staked_units")]
    public double TotalStakedUnits { get; init; }

    [JsonPropertyName("total_returned_units")]
    public double TotalReturnedUnits { get; init; }

    [JsonPropertyName("pl_units")]
    public double PlUnits { get; init; }

    [JsonPropertyName("roi_percent")]
    public double RoiPercent { get; init; }

    [JsonPropertyName("win_rate")]
    public double WinRate { get; init; }

    [JsonPropertyName("leg_hit_rate")]
    public double LegHitRate { get; init; }
}

public sealed class MarketAccuracy
{
    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
    
    [JsonPropertyName("correct")]
    public int Correct { get; init; }
}

public sealed class LeagueAccuracy
{
    [JsonPropertyName("league")]
    public string League { get; init; } = string.Empty;

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
    
    [JsonPropertyName("correct")]
    public int Correct { get; init; }
}

public sealed class LeagueMarketAccuracy
{
    [JsonPropertyName("league")]
    public string League { get; init; } = string.Empty;

    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
    
    [JsonPropertyName("correct")]
    public int Correct { get; init; }
}
