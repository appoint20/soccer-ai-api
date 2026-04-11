using Mediator.Net.Contracts;
using System.Text.Json.Serialization;

namespace SoccerAi.Application.Features.Combinations;

public class GetMatchCombinationQuery(DateTimeOffset date, string language = "en", bool refresh = false) : IRequest
{
    public DateTimeOffset Date { get; } = date;
    public string Language { get; } = language;
    public bool Refresh { get; } = refresh;
}

public class GetMatchCombinationResponse(List<CombinationDto> combinations) : IResponse
{
    [JsonPropertyName("combinations")]
    public List<CombinationDto> Combinations { get; } = combinations;
}

public sealed class CombinationDto
{
    [JsonPropertyName("combination_id")]
    public int CombinationId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("total_odds")]
    public double TotalOdds { get; init; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("matches")]
    public List<CombinationMatchDto> Matches { get; init; } = [];

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";
}

public sealed class CombinationMatchDto
{
    [JsonPropertyName("fixture_id")]
    public int FixtureId { get; init; }

    [JsonPropertyName("league")]
    public string League { get; init; } = "";

    [JsonPropertyName("home_team")]
    public string HomeTeam { get; init; } = "";

    [JsonPropertyName("away_team")]
    public string AwayTeam { get; init; } = "";

    [JsonPropertyName("selection")]
    public string Selection { get; init; } = "";

    [JsonPropertyName("odds")]
    public double Odds { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;
}