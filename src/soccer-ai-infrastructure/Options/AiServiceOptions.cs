namespace SoccerAi.Infrastructure.Options;

public sealed class AiServiceOptions
{
    /// <summary>Base URL of the Z.ai chat completions endpoint.</summary>
    public string BaseUrl { get; set; } = "https://api.z.ai/api/paas/v4/chat/completions";

    /// <summary>Default Z.ai model to use.</summary>
    public string DefaultModel { get; set; } = "glm-5.1";

    /// <summary>HTTP timeout in seconds for the external AI provider.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>API key for the external AI provider.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
