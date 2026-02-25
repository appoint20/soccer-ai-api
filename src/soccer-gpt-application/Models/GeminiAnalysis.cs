using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public sealed class GeminiAnalysis
{
    public string Recommendation { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public bool IsTrap { get; init; }
    public string TrapReason { get; init; } = string.Empty;
    public string OneLineSummary { get; init; } = string.Empty;
    [JsonIgnore]
    public string BttsSummary { get; init; } = string.Empty;
    [JsonIgnore]
    public string Over25Summary { get; init; } = string.Empty;
    [JsonIgnore]
    public string Under25Summary { get; init; } = string.Empty;
    [JsonIgnore]
    public string HomeWinSummary { get; init; } = string.Empty;
    [JsonIgnore]
    public string AwayWinSummary { get; init; } = string.Empty;
}
