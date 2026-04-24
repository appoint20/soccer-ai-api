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
    
    // ── AI Decision Layer (per-market qualifications) ────────────
    public bool AiOver25Qualified { get; init; }
    public bool AiBttsQualified { get; init; }
    public bool AiUnder25Qualified { get; init; }
    public bool AiGoals23Qualified { get; init; }
    public bool AiHomeWinQualified { get; init; }
    public bool AiAwayWinQualified { get; init; }
    public string AiBestBet { get; init; } = "";
    public int AiOverallConfidence { get; init; }
    
    /// <summary>True if this entity was processed by the AI Decision Layer (has per-market decisions).</summary>
    public bool HasDecisionLayer => AiOverallConfidence > 0;
}
