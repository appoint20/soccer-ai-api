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
    public string Decision { get; set; } = "INSUFFICIENT_DATA"; // VALID, INVALID, INSUFFICIENT_DATA
    
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
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
                MATCH DATA:
                {matchJson}

                PROPOSED PREDICTION:
                '{proposedPrediction}'

                Validate the prediction now.")
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
                ?? new DecisionLayerResult { Decision = "ERROR", Reason = "Null response from AI" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiDecisionLayerService.ValidatePredictionAsync failed");
            return new DecisionLayerResult { Decision = "ERROR", Reason = ex.Message };
        }
    }

    private static class Prompts
    {
        public const string DecisionLayerSystemPrompt = @"
            You are a strict Data Validation Engine. You do NOT speculate.
            Your job is to validate if a 'Proposed Prediction' is supported by the 'Match Data'.
            
            RULES:
            1. Analyze the 'Match Data' JSON strictly.
            2. Compare it against the 'Proposed Prediction'.
            3. If the data supports the prediction, output: { ""decision"": ""VALID"", ""reason"": ""..."" }
            4. If the data contradicts the prediction, output: { ""decision"": ""INVALID"", ""reason"": ""..."" }
            5. If data is insufficient, output: { ""decision"": ""INSUFFICIENT_DATA"", ""reason"": ""..."" }
            
            Output ONLY valid JSON. No intro text, no explanation outside the JSON.";
    }
}
