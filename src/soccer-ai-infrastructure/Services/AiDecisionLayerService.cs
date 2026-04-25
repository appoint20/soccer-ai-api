using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

public class DecisionLayerResult
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "INSUFFICIENT_DATA"; // VALID, INVALID, OVERRIDE_MODEL, INSUFFICIENT_DATA
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("actual_prediction")]
    public string ActualPrediction { get; set; } = string.Empty;
    
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    // Backward compatibility property for internal logging/mapping
    [JsonIgnore]
    public string Reason => Reasoning;
}

public interface IAiDecisionLayerService
{
    Task<DecisionLayerResult> ValidatePredictionAsync(string matchJson, string proposedPrediction);
    Task<Dictionary<string, DecisionLayerResult>> ValidateMarketsAsync(string matchJson, List<KeyValuePair<string, string>> proposals);
    
    /// <summary>
    /// Full AI Decision Layer: Sends enriched match data and gets back per-market qualification decisions.
    /// This is the primary entry point for the AI-driven decision flow.
    /// </summary>
    Task<AiFullDecisionResult?> EvaluateMatchAsync(string enrichedMatchJson);
}

public sealed class AiDecisionLayerService : IAiDecisionLayerService
{
    private readonly ChatClient _client;
    private readonly ILogger<AiDecisionLayerService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public AiDecisionLayerService(
        IOptions<AiServiceOptions> options,
        ChatClient client,
        ILogger<AiDecisionLayerService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<DecisionLayerResult> ValidatePredictionAsync(string matchJson, string proposedPrediction)
    {
        var proposals = new List<KeyValuePair<string, string>> { new("default", proposedPrediction) };
        var results = await ValidateMarketsAsync(matchJson, proposals);
        return results.TryGetValue("default", out var result) ? result : new DecisionLayerResult { Decision = "ERROR" };
    }

    public async Task<Dictionary<string, DecisionLayerResult>> ValidateMarketsAsync(string matchJson, List<KeyValuePair<string, string>> proposals)
    {
        var proposalsText = string.Join("\n", proposals.Select(p => $"[{p.Key}]: {p.Value}"));
        
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(Prompts.LegacyDecisionLayerPrompt),
            new UserChatMessage($@"
                Analyze this match data:
                {matchJson}

                CURRENT PROPOSALS TO VALIDATE:
                {proposalsText}
                
                Respond for EACH key provided in the format: {{ ""key"": {{ decision, confidence, reasoning }} }}")
        };

        return await CallWithRetry<Dictionary<string, DecisionLayerResult>>(messages) ?? new();
    }

    /// <summary>
    /// Full AI Decision Layer — sends enriched match data, receives per-market decisions.
    /// </summary>
    public async Task<AiFullDecisionResult?> EvaluateMatchAsync(string enrichedMatchJson)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(Prompts.FullDecisionLayerPrompt),
            new UserChatMessage($"Evaluate this match and decide all markets:\n\n{enrichedMatchJson}")
        };

        return await CallWithRetry<AiFullDecisionResult>(messages);
    }

    private async Task<T?> CallWithRetry<T>(List<ChatMessage> messages) where T : class
    {
        int maxRetries = 3;
        int delayMs = 2000;
        var random = new Random();

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 2000,
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
                };

                var completion = await _client.CompleteChatAsync(messages, options);
                var rawContent = completion.Value.Content[0].Text;

                return JsonSerializer.Deserialize<T>(rawContent, JsonOpts);
            }
            catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limit"))
            {
                if (i == maxRetries)
                {
                    _logger.LogError(ex, "AiDecisionLayerService: Final retry failed due to rate limit.");
                    throw;
                }

                int backoff = delayMs * (int)Math.Pow(2, i) + random.Next(0, 1000);
                _logger.LogWarning("[AiDecision] Rate limit hit (429). Retrying in {Ms}ms... (Attempt {Attempt}/{Max})", backoff, i + 1, maxRetries);
                await Task.Delay(backoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AiDecisionLayerService call failed");
                return null;
            }
        }

        return null;
    }

    private static class Prompts
    {
        public const string FullDecisionLayerPrompt = @"
You are a Senior Football Analyst and Decision Engine for a professional betting platform.

You receive structured match data including:
- Team statistics (attack/defense strength, form, possession, momentum, clean sheet rate)
- Head-to-head history (BTTS rate, Over 2.5 rate, average goals per game)
- League standings (rank, points, games played, relegation zone)
- Mathematical model probabilities (Poisson, Monte Carlo)
- Market odds from bookmakers
- Mathematical rule engine proposals (what the math model recommends)

YOUR JOB: Make the FINAL qualification decision for each betting market.
The mathematical models propose — YOU decide. You must be BALANCED — do not favor defensive markets over offensive ones.

QUALIFICATION RULES (apply equally to ALL markets):

BTTS / OVER 2.5 QUALIFICATION:
1. If BOTH teams average >= 1.0 goals scored in last 7 AND BTTS rate in last 3 is >= 0.5 → QUALIFY BTTS.
2. If combined average goals (home scored + away scored last 7) >= 2.5 → QUALIFY Over 2.5.
3. If H2H shows BTTS in 3/5+ meetings or avg total goals >= 2.5 → strong BTTS/Over 2.5 signal.
4. If BTTS probability >= 55% AND both teams have attack strength >= 1.0 → QUALIFY BTTS.

UNDER 2.5 / LOW SCORING QUALIFICATION:
5. If BOTH teams average < 0.8 goals scored in last 7 → QUALIFY Under 2.5.
6. If one team has clean sheet rate > 60% AND the other has attack strength < 0.8 → QUALIFY Under 2.5.
7. REJECT Under 2.5 if combined average goals > 2.5 or if H2H avg total goals > 2.5.

MATCH WINNER:
8. Confidence must be >= 60% and the team must show clear dominance (form >= 60%, rank advantage, higher attack strength).

TRAP DETECTION:
9. If a team is in the relegation zone (bottom 3-4) late in season (30+ games played) → FLAG as trap.
10. If H2H contradicts the model strongly (e.g., model says Under but H2H avg is 3.5 goals) → FLAG as warning.

GENERAL RULES:
11. Cross-reference model probabilities against raw stats. When they conflict, trust raw averages.
12. Look for VALUE: high confidence + good bookmaker odds = qualified.
13. Multiple markets CAN be qualified simultaneously (e.g., BTTS AND Over 2.5 can both be true).
14. Do NOT default to Under 2.5. Evaluate each market on its own merit.

FOR EACH MARKET, decide:
- qualified: true/false (should we bet on this?)
- confidence: 0-100 (minimum 55 to qualify)
- reasoning: 1-2 sentences max explaining your decision

OUTPUT FORMAT (STRICT JSON, nothing else):
{
  ""over25"": { ""qualified"": true, ""confidence"": 72, ""reasoning"": ""Combined avg goals 2.8, H2H avg 3.2."" },
  ""btts"": { ""qualified"": true, ""confidence"": 68, ""reasoning"": ""Both teams avg > 1.0 goals, BTTS rate 67% in last 3."" },
  ""under25"": { ""qualified"": false, ""confidence"": 30, ""reasoning"": ""High scoring profile contradicts Under 2.5."" },
  ""goals23"": { ""qualified"": true, ""confidence"": 65, ""reasoning"": ""Expected total 2.4-2.8 goals."" },
  ""home_win"": { ""qualified"": false, ""confidence"": 48, ""reasoning"": ""Home team has inconsistent form."" },
  ""away_win"": { ""qualified"": false, ""confidence"": 38, ""reasoning"": ""Away team low win rate."" },
  ""trap"": { ""is_trap"": false, ""reason"": """" },
  ""best_bet"": ""BTTS"",
  ""overall_confidence"": 68
}

CRITICAL RULES:
- Do NOT hallucinate data. Use ONLY what is provided.
- Output ONLY valid JSON. No intro text, no explanation outside the JSON.
- Be BALANCED and data-driven. Do not have a bias toward any specific market.
- Minimum 55% confidence required for any market to be qualified.
- best_bet must be the single strongest market you identified.
- It is EQUALLY valid to qualify BTTS/Over 2.5 as it is to qualify Under 2.5. Let the DATA decide.";

        // Legacy prompt kept for backward compatibility with ValidateMarketsAsync
        public const string LegacyDecisionLayerPrompt = @"
            You are a Senior Risk Analyst for a sports betting firm. Your job is to spot 'Traps' where the data is misleading.
            
            You will be given a JSON object. Perform these steps strictly:
            
            1. **The Eye Test (Stats Check):**
               - Look at 'away_stats.avg_goals_scored_last_7' and 'home_stats.avg_goals_conceded_last_7'.
               - If the Away team averages > 2.0 goals, the likelihood of a LOW SCORING game is statistically impossible.
            
            2. **The Model Conflict Check:**
               - Compare the models' probability (e.g., Over 2.5) with the raw average goals.
               - If the raw averages suggest High Scoring (Total > 2.5) but the Model Probability is < 50%, this is a 'VALUE TRAP'. 
               - **Rule:** In a conflict between raw averages and models, TRUST the raw averages.
            
            3. **Trap Analysis:**
               - If 'trap.is_trap' is true, analyze the 'reason'.
               - Does the trap mean the favorite will LOSE, or just that the ODDS are bad?
               - Example: If the trap is 'Liverpool poor away record', it implies they might lose, NOT that they won't score.
            
            4. **Final Verdict:**
               - Output a JSON object with your final decision.
               - If the data suggests a high score despite models saying otherwise, flag 'OVERRIDE_MODEL'.
            
            Output Format (JSON only):
            {
                ""decision"": ""VALID"" | ""INVALID"" | ""OVERRIDE_MODEL"" | ""INSUFFICIENT_DATA"",
                ""confidence"": 0-100,
                ""actual_prediction"": ""Home Win"" | ""Away Win"" | ""Over 2.5"" | ""Under 2.5"",
                ""reasoning"": ""Explain specifically why you overruled the models or the trap.""
            }
            
            Output ONLY valid JSON. No intro text, no explanation outside the JSON.";
    }
}
