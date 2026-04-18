using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Constants;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Direct implementation of IAiAnalysisService calling the Z.ai (GLM-5.1) API.
/// This replaces the local Python microservice.
/// </summary>
public sealed class ZaiAnalysisService : IAiAnalysisService
{
    private readonly HttpClient _http;
    private readonly AiServiceOptions _options;
    private readonly ILogger<ZaiAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ZaiAnalysisService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiServiceOptions> options,
        ILogger<ZaiAnalysisService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = httpClientFactory.CreateClient("ZaiClient");
        
        // Ensure BaseUrl is valid
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogError("Z.ai BaseUrl is not configured.");
        }
    }

    public async Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items)
    {
        if (items == null || items.Count == 0) return new();

        var systemPrompt = AiPrompts.MatchAnalysisSystemPrompt;
        var userContent = $"MATCH BATCH DATA (JSON):\n{JsonSerializer.Serialize(items, JsonOpts)}";

        try
        {
            var rawJson = await CallZaiAsync(systemPrompt, userContent);
            if (string.IsNullOrWhiteSpace(rawJson)) return new();

            var results = JsonSerializer.Deserialize<List<AiBilingualResult>>(rawJson, JsonOpts);
            return results?.ToDictionary(r => r.FixtureId) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZaiAnalysisService.AnalyzeBatchAsync failed");
            return new();
        }
    }

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates)
    {
        if (candidates == null || candidates.Count == 0) return new();

        var systemPrompt = AiPrompts.BuildCombinationsSystemPrompt;
        var userContent = $"MATCH DATA (JSON):\n{JsonSerializer.Serialize(candidates, JsonOpts)}";

        try
        {
            var rawJson = await CallZaiAsync(systemPrompt, userContent);
            if (string.IsNullOrWhiteSpace(rawJson)) return new();

            return JsonSerializer.Deserialize<List<CombinationDto>>(rawJson, JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZaiAnalysisService.BuildCombinationsAsync failed");
            return new();
        }
    }

    public async Task<ChatCombinationIntent?> ParseChatIntentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var systemPrompt = AiPrompts.ParseIntentSystemPrompt;
        var userContent = $"USER QUERY: \"{query}\"";

        try
        {
            var rawJson = await CallZaiAsync(systemPrompt, userContent);
            if (string.IsNullOrWhiteSpace(rawJson)) return null;

            return JsonSerializer.Deserialize<ChatCombinationIntent>(rawJson, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZaiAnalysisService.ParseChatIntentAsync failed");
            return null;
        }
    }

    private async Task<string?> CallZaiAsync(string systemPrompt, string userContent)
    {
        var request = new ZaiChatRequest
        {
            Model = _options.DefaultModel,
            Messages =
            [
                new ZaiMessage { Role = "system", Content = systemPrompt },
                new ZaiMessage { Role = "user", Content = userContent }
            ],
            Temperature = 0.1
        };

        var response = await _http.PostAsJsonAsync("", request, JsonOpts);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ZaiChatResponse>(JsonOpts);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content)) return null;

        // Clean up markdown code fences if the model included them
        return CleanJsonResponse(content);
    }

    private static string CleanJsonResponse(string content)
    {
        var cleaned = content.Trim();
        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned.Substring(7);
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3);
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }

        return cleaned.Trim();
    }

    // ─── Z.ai API Models ─────────────────────────────────────────────────────

    private class ZaiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "glm-5.1";

        [JsonPropertyName("messages")]
        public List<ZaiMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.1;
    }

    private class ZaiMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class ZaiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ZaiChoice> Choices { get; set; } = new();
    }

    private class ZaiChoice
    {
        [JsonPropertyName("message")]
        public ZaiMessage Message { get; set; } = default!;
    }
}
