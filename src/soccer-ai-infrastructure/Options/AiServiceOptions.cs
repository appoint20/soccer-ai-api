namespace SoccerAi.Infrastructure.Options;

public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>Base URL for OpenRouter / OpenAI-compatible API endpoint.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Primary model, as an OpenRouter slug.
    /// </summary>
    /// <remarks>
    /// `stealth/ox-alpha` is a cloaked preview model: free while the preview
    /// lasts, anonymous operator, and it can be withdrawn without notice. That
    /// is exactly why <see cref="FallbackModel"/> is a named, paid model —
    /// when the stealth slug stops resolving, narratives keep being written
    /// instead of the sync silently producing none.
    /// </remarks>
    public string DefaultModel { get; set; } = "stealth/ox-alpha";

    /// <summary>Used when the primary model is unavailable.</summary>
    public string FallbackModel { get; set; } = "anthropic/claude-sonnet-5";

    /// <summary>
    /// HTTP timeout in seconds for inference calls. Applied to the client
    /// pipeline; without that the SDK silently uses its own 100-second default.
    /// </summary>
    /// <remarks>
    /// A reasoning model spends most of the request thinking before it emits
    /// anything, so this is generous on purpose.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 240;

    /// <summary>
    /// Retries per model before moving to the fallback. Deliberately low: the
    /// usual failure here is a timeout, and retrying a timeout three times just
    /// multiplies the wait.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Output cap per request.</summary>
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>
    /// Consecutive primary-model failures before the run stops trying it and
    /// goes straight to the fallback. 0 disables the circuit breaker.
    /// </summary>
    public int PrimaryFailuresBeforeSkip { get; set; } = 3;

    /// <summary>
    /// OpenRouter's per-request reasoning control, sent as the `reasoning`
    /// body field. The SDK has no property for it, so
    /// <c>OpenRouterReasoningPolicy</c> injects it.
    /// </summary>
    public AiReasoningOptions Reasoning { get; set; } = new();

    /// <summary>API Key (resolved from config or OPENROUTER_API_KEY env var).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Whether the AI service is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Maps to OpenRouter's `reasoning` object.
/// </summary>
/// <remarks>
/// Worth turning off — or capping — when a batch keeps timing out: a reasoning
/// model spends the whole budget thinking before it emits its first token, and
/// that thinking is what pushes a request past the timeout.
/// </remarks>
public sealed class AiReasoningOptions
{
    /// <summary>
    /// Whether to send the field at all. False leaves the provider default in
    /// place, which is what a non-reasoning model wants.
    /// </summary>
    public bool Send { get; set; } = true;

    /// <summary>Value of `reasoning.enabled`.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>`low`, `medium` or `high`. Null leaves it to the provider.</summary>
    public string? Effort { get; set; }

    /// <summary>Hard cap on thinking tokens. Null leaves it to the provider.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Keep the reasoning trace out of the response. On by default: only the
    /// JSON payload is parsed, so the trace is bytes over the wire nobody reads.
    /// </summary>
    public bool Exclude { get; set; } = true;
}
