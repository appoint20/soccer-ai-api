
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public record LeagueDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("api_id")]
    public int ApiId { get; init; }

    [JsonPropertyName("folder_name")]
    public string FolderName { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = string.Empty;

    [JsonPropertyName("logo")]
    public string Logo { get; init; } = string.Empty;

    [JsonPropertyName("teams_count")]
    public int TeamsCount { get; init; }

    [JsonPropertyName("matchday")]
    public int Matchday { get; init; }

    [JsonPropertyName("start_date")]
    public string StartDate { get; init; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; init; } = string.Empty;
}
