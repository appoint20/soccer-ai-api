using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

public sealed class AiAnalysisDto
{
    public string Recommendation { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public bool IsTrap { get; init; }
    public string TrapReason { get; init; } = string.Empty;
    public string OneLineSummary { get; init; } = string.Empty;
    public string BttsSummary { get; init; } = string.Empty;
    public string Over25Summary { get; init; } = string.Empty;
    public string Under25Summary { get; init; } = string.Empty;
    public string HomeWinSummary { get; init; } = string.Empty;
    public string AwayWinSummary { get; init; } = string.Empty;
}
