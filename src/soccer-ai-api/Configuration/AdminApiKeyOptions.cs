namespace SoccerAi.Api.Configuration;

public sealed class AdminApiKeyOptions
{
    public const string SectionName = "AdminApi";

    /// <summary>
    /// SHA-256 hashes (hex) of allowed API keys, for when the key itself should
    /// not sit in configuration. Supports rotation without redeploying code.
    /// </summary>
    public string[] ApiKeyHashes { get; init; } = [];

    /// <summary>
    /// Raw keys, hashed at startup. Set one of these and it works — there is no
    /// need to compute a digest by hand. Only ever held in memory as a hash.
    /// </summary>
    public string[] ApiKeys { get; init; } = [];

    public string HeaderName { get; init; } = "X-API-Key";

    /// <summary>
    /// Rejects trivially short keys. Any shape is accepted above this length;
    /// keys are no longer required to be GUIDs.
    /// </summary>
    public int MinimumKeyLength { get; init; } = 16;
}
