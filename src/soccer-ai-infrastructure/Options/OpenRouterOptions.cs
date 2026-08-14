namespace SoccerAi.Infrastructure.Options;

/// <summary>
/// Configuration for the language-model forecasts run alongside the pipeline
/// ("OpenRouter" section). One gateway, several models, scored against each
/// other and against the statistical model.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Read from configuration, then OPENROUTER_API_KEY. Empty disables
    /// forecasting rather than failing the sync.
    /// </summary>
    public string ApiKey { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Model slugs exactly as OpenRouter lists them. Deliberately left empty:
    /// a wrong slug is a per-request 404 that only shows up in the logs, so
    /// these are configured explicitly rather than guessed at in code.
    /// </summary>
    public List<string> Models { get; set; } = [];

    /// <summary>
    /// Sent as HTTP-Referer and X-Title. OpenRouter uses these for attribution
    /// on its dashboard; they do not affect routing.
    /// </summary>
    public string AppUrl { get; set; } = "https://soccer-ai-api.onrender.com";
    public string AppTitle { get; set; } = "Soccer AI";

    public int MaxTokens { get; set; } = 2048;

    /// <summary>Only forecast fixtures kicking off within this window.</summary>
    public int MaxDaysAhead { get; set; } = 7;

    public int TimeoutSeconds { get; set; } = 120;
}
