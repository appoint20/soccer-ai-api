using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Replaces the legacy Gemini analysis path by calling the local Python FastAPI microservice
/// (Mistral-7B or LLaMA-3) via plain HTTP.
///
/// Implements the IAiAnalysisService interface.
/// </summary>
public sealed class LocalAiAnalysisService : IAiAnalysisService
{
    private readonly HttpClient _http;
    private readonly AiServiceOptions _options;
    private readonly ILogger<LocalAiAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LocalAiAnalysisService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiServiceOptions> options,
        ILogger<LocalAiAnalysisService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = httpClientFactory.CreateClient("AiService");
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _http.DefaultRequestHeaders.Add("X-AI-Model", _options.DefaultModel);
    }

    // ─── Model selector helper ───────────────────────────────────────────────

    /// <summary>
    /// Temporarily override the model for a single call by cloning the request
    /// with a different X-AI-Model header value.
    /// </summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object body, string? modelOverride = null)
    {
        var req = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        req.Headers.Add("X-AI-Model", modelOverride ?? _options.DefaultModel);
        return req;
    }

    // ─── AnalyzeBatchAsync ───────────────────────────────────────────────────

    public async Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items)
    {
        if (items == null || items.Count == 0)
            return new();

        _logger.LogInformation("[LocalAI] AnalyzeBatch: {Count} items using model {Model}", items.Count, _options.DefaultModel);

        try
        {
            var payload = new { items };
            var req = BuildRequest(HttpMethod.Post, "analyze", payload);
            var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content.ReadFromJsonAsync<AnalyzeEnvelope>(JsonOpts);
            
            return envelope?.Results == null 
                ? new Dictionary<int, AiBilingualResult>() 
                : envelope.Results.ToDictionary(r => r.FixtureId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LocalAI] AnalyzeBatch failed");
            return new();
        }
    }

    // ─── BuildCombinationsAsync ──────────────────────────────────────────────

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return new();

        _logger.LogInformation("[LocalAI] BuildCombinations: {Count} candidates", candidates.Count);

        try
        {
            var payload = new { candidates };
            var req = BuildRequest(HttpMethod.Post, "build-combinations", payload);
            var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content.ReadFromJsonAsync<CombinationsEnvelope>(JsonOpts);
            return envelope?.Combinations ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LocalAI] BuildCombinations failed");
            return new();
        }
    }

    // ─── ParseChatIntentAsync ────────────────────────────────────────────────

    public async Task<ChatCombinationIntent?> ParseChatIntentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        _logger.LogInformation("[LocalAI] ParseChatIntent using model {Model}", _options.DefaultModel);

        try
        {
            var payload = new { query };
            var req = BuildRequest(HttpMethod.Post, "parse-intent", payload);
            var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ChatCombinationIntent>(JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LocalAI] ParseChatIntent failed, returning null (fallback will apply)");
            return null;
        }
    }

    // ─── Private envelope types ──────────────────────────────────────────────

    private sealed class AnalyzeEnvelope
    {
        [JsonPropertyName("results")]
        public List<AiBilingualResult> Results { get; set; } = new();
    }

    private sealed class CombinationsEnvelope
    {
        [JsonPropertyName("combinations")]
        public List<CombinationDto> Combinations { get; set; } = new();
    }
}
