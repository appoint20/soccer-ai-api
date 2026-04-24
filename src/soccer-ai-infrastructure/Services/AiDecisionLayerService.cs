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
- Rest days since each team's last match

YOUR JOB: Make the FINAL qualification decision for each betting market.
The mathematical models propose — YOU decide. You are the last line of defense.

DECISION RULES:
1. ALWAYS cross-reference model probability against raw team stats and H2H history.
2. If model says Over 2.5 but both teams average < 1.0 goals in last 7 → REJECT.
3. If model says BTTS but one team has clean sheet rate > 50% → REJECT.
4. If a team is in the relegation zone (bottom 3-4) late in season (30+ games played) → FLAG as trap. Warn: 'No motivation due to relegation (Abstieg). Results unpredictable.'
5. If H2H shows a strong trend (e.g., BTTS in 4/5 meetings or avg 3+ goals) → can OVERRIDE model.
6. Trust raw averages over model probabilities when they conflict.
7. Look for VALUE: compare your confidence against bookmaker odds. High confidence + good odds = qualified.
8. If both BTTS probability < 50% AND Over 2.5 probability < 50%, lean toward Under 2.5 / Low Scoring.
9. For Match Winner, confidence must be >= 60% and the team must show clear dominance (form, rank, attack strength).

FOR EACH MARKET, decide:
- qualified: true/false (should we bet on this?)
- confidence: 0-100 (how confident are you? minimum 60 to qualify)
- reasoning: 1-2 sentences max explaining your decision

OUTPUT FORMAT (STRICT JSON, nothing else):
{
  ""over25"": { ""qualified"": false, ""confidence"": 42, ""reasoning"": ""Both teams average under 1.5 goals."" },
  ""btts"": { ""qualified"": false, ""confidence"": 35, ""reasoning"": ""Home team keeps clean sheets 50% of the time."" },
  ""under25"": { ""qualified"": true, ""confidence"": 68, ""reasoning"": ""Low scoring profile confirmed by stats and H2H."" },
  ""goals23"": { ""qualified"": true, ""confidence"": 72, ""reasoning"": ""Expected 2-3 goals based on averages."" },
  ""home_win"": { ""qualified"": false, ""confidence"": 32, ""reasoning"": ""Home team lacks dominance."" },
  ""away_win"": { ""qualified"": false, ""confidence"": 36, ""reasoning"": ""Away team inconsistent form."" },
  ""trap"": { ""is_trap"": false, ""reason"": """" },
  ""best_bet"": ""Under 2.5 Goals"",
  ""overall_confidence"": 68
}

CRITICAL RULES:
- Do NOT hallucinate data. Use ONLY what is provided.
- Output ONLY valid JSON. No intro text, no explanation outside the JSON.
- Be conservative: when in doubt, set qualified=false.
- Minimum 60% confidence required for any market to be qualified.
- best_bet must be the single strongest market you identified.";

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
