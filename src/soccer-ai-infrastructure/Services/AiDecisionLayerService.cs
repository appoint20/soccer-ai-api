using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
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
}

public sealed class AiDecisionLayerService : IAiDecisionLayerService
{
    private readonly ChatClient _client;
    private readonly ILogger<AiDecisionLayerService> _logger;

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
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(Prompts.DecisionLayerSystemPrompt),
            new UserChatMessage($@"
                Analyze this match data:
                {matchJson}

                CURRENT PROPOSAL:
                '{proposedPrediction}'")
        };

        try
        {
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1000,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var completion = await _client.CompleteChatAsync(messages, options);
            var rawContent = completion.Value.Content[0].Text;

            return JsonSerializer.Deserialize<DecisionLayerResult>(rawContent) 
                ?? new DecisionLayerResult { Decision = "ERROR", Reasoning = "Null response from AI" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiDecisionLayerService.ValidatePredictionAsync failed");
            return new DecisionLayerResult { Decision = "ERROR", Reasoning = ex.Message };
        }
    }

    private static class Prompts
    {
        public const string DecisionLayerSystemPrompt = @"
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
