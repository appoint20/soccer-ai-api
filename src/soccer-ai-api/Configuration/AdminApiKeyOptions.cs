namespace SoccerAi.Api.Configuration;

public sealed class AdminApiKeyOptions
{
    public const string SectionName = "AdminApi";

    /// <summary>
    /// SHA-256 hashes (hex) of allowed GUID API keys.
    /// Supports key rotation without redeploying code.
    /// </summary>
    public string[] ApiKeyHashes { get; init; } = [];

    public string HeaderName { get; init; } = "X-API-Key";
}
