
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class PagedResponse<T>
{
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = new();

    [JsonPropertyName("summary")]
    public ResponseSummary Summary { get; set; } = new();
}

public class ResponseSummary
{
    [JsonPropertyName("total_stake")]
    public decimal TotalStake { get; set; }
}
