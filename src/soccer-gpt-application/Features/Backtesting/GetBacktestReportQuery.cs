using Mediator.Net.Contracts;
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Features.Backtesting;

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
