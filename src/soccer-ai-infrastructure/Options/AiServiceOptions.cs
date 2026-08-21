namespace SoccerAi.Infrastructure.Options;

public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>Base URL for OpenRouter / OpenAI-compatible API endpoint.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Primary model to use (e.g. "anthropic/claude-3.5-sonnet").</summary>
    public string DefaultModel { get; set; } = "anthropic/claude-3.5-sonnet";

    /// <summary>Fallback model if primary model is unavailable (e.g. "stealth/ox-alpha").</summary>
    public string FallbackModel { get; set; } = "stealth/ox-alpha";

    /// <summary>HTTP timeout in seconds for inference calls.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>API Key (resolved from config or OPENROUTER_API_KEY env var).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Whether the AI service is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

