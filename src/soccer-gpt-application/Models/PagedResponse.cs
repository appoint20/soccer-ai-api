using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class PagedResponse<T>
{
    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("items")]
    public List<T> Items { get; init; } = [];
}