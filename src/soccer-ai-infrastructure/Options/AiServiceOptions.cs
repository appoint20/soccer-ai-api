namespace SoccerAi.Infrastructure.Options;

public sealed class AiServiceOptions
{
    /// <summary>Base URL of the Python inference microservice, e.g. http://localhost:8101</summary>
    public string BaseUrl { get; set; } = "http://localhost:8101";

    /// <summary>Default model to use: "glm-5.1" or "mistral"</summary>
    public string DefaultModel { get; set; } = "glm-5.1";

    /// <summary>HTTP timeout in seconds for inference calls (can be slow on CPU).</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>API Key for cloud providers like Z.ai or Gemini.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Whether the AI service is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
