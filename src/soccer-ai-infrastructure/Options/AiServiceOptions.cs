namespace SoccerAi.Infrastructure.Options;

public sealed class AiServiceOptions
{
    /// <summary>Base URL of the Python inference microservice, e.g. http://localhost:8100</summary>
    public string BaseUrl { get; set; } = "http://localhost:8100";

    /// <summary>Default model to use: "mistral" or "llama3"</summary>
    public string DefaultModel { get; set; } = "mistral";

    /// <summary>HTTP timeout in seconds for inference calls (can be slow on CPU).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// When true the Python microservice is used instead of Gemini.
    /// Set to false to keep using Gemini (e.g. in production before the service is deployed).
    /// </summary>
    public bool Enabled { get; set; } = false;
}
