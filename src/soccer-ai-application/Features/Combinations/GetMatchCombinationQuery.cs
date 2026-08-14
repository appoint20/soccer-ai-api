using Mediator.Net.Contracts;
using SoccerAi.Application.Models;
using System.Text.Json.Serialization;

namespace SoccerAi.Application.Features.Combinations;

public class GetMatchCombinationQuery : PageRequest, IRequest
{
    public GetMatchCombinationQuery(
        DateTimeOffset date, string language = "en", bool refresh = false, string? userMessage = null)
    {
        Date = date;
        Language = language;
        Refresh = refresh;
        UserMessage = userMessage;
    }

    public DateTimeOffset Date { get; init; }
    public string Language { get; init; } = "en";
    public bool Refresh { get; init; }
    public string? UserMessage { get; init; }
}

/// <summary>
/// Paged combinations.
///
/// <c>combinations</c> duplicates <c>items</c> and is deprecated; it stays for
/// one release so existing callers survive the cutover.
/// </summary>
public class GetMatchCombinationResponse : PagedResponse<CombinationDto>, IResponse
{
    public GetMatchCombinationResponse() { }

    public GetMatchCombinationResponse(List<CombinationDto> combinations, int limit, int offset)
    {
        Total = combinations.Count;
        Limit = limit;
        Offset = offset;
        Items = [.. combinations.Skip(offset).Take(limit)];
    }

    /// <summary>Deprecated — use <c>items</c>.</summary>
    [JsonPropertyName("combinations")]
    public List<CombinationDto> Combinations => Items;
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

    [JsonPropertyName("won_count")]
    public int WonCount { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

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
    public double? Odds { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "Pending"; // Win, Loss, Pending

    [JsonPropertyName("status")]
    public string Status { get; set; } = "NS";

    [JsonPropertyName("home_goals")]
    public int? HomeGoals { get; set; }

    [JsonPropertyName("away_goals")]
    public int? AwayGoals { get; set; }
}