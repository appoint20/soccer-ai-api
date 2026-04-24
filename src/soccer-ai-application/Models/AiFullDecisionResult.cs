using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// Result from the AI Decision Layer for a single market.
/// </summary>
public sealed class AiMarketDecision
{
    [JsonPropertyName("qualified")]
    public bool Qualified { get; set; }
    
    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }
    
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// Trap detection result from the AI Decision Layer.
/// </summary>
public sealed class AiTrapDecision
{
    [JsonPropertyName("is_trap")]
    public bool IsTrap { get; set; }
    
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>
/// Full AI Decision Layer response — one per match.
/// The AI evaluates ALL markets and returns structured decisions.
/// </summary>
public sealed class AiFullDecisionResult
{
    [JsonPropertyName("over25")]
    public AiMarketDecision Over25 { get; set; } = new();
    
    [JsonPropertyName("btts")]
    public AiMarketDecision Btts { get; set; } = new();
    
    [JsonPropertyName("under25")]
    public AiMarketDecision Under25 { get; set; } = new();
    
    [JsonPropertyName("goals23")]
    public AiMarketDecision Goals23 { get; set; } = new();
    
    [JsonPropertyName("home_win")]
    public AiMarketDecision HomeWin { get; set; } = new();
    
    [JsonPropertyName("away_win")]
    public AiMarketDecision AwayWin { get; set; } = new();
    
    [JsonPropertyName("trap")]
    public AiTrapDecision Trap { get; set; } = new();
    
    [JsonPropertyName("best_bet")]
    public string BestBet { get; set; } = "";
    
    [JsonPropertyName("overall_confidence")]
    public int OverallConfidence { get; set; }
}
