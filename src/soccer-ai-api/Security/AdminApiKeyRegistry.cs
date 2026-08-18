using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SoccerAi.Api.Configuration;

namespace SoccerAi.Api.Security;

/// <summary>
/// The set of accepted admin keys, resolved once from every supported source.
/// </summary>
/// <remarks>
/// Keys arrive either raw or pre-hashed, from configuration or the environment,
/// and every source is additive: setting an environment variable adds a key
/// rather than replacing the configured ones.
///
/// The environment used to be consulted only when configuration held no hashes
/// at all, so ADMIN_API_KEY_HASH did nothing on any deployment whose
/// appsettings.json shipped a hash — which is every deployment. Setting it
/// looked like it should work and silently didn't.
/// </remarks>
public sealed class AdminApiKeyRegistry
{
    /// <summary>A raw key. Hashed here; never stored or logged in the clear.</summary>
    public const string RawKeyEnvVar = "ADMIN_API_KEY";

    /// <summary>A pre-computed SHA-256 hex digest, for keeping the key off the host.</summary>
    public const string HashEnvVar = "ADMIN_API_KEY_HASH";

    private static readonly char[] Separators = [',', ';'];

    private readonly HashSet<string> _hashes;

    public AdminApiKeyRegistry(IOptions<AdminApiKeyOptions> options)
        : this(options.Value,
               Environment.GetEnvironmentVariable(RawKeyEnvVar),
               Environment.GetEnvironmentVariable(HashEnvVar))
    {
    }

    /// <summary>Environment values are passed in so tests need not mutate the process.</summary>
    public AdminApiKeyRegistry(AdminApiKeyOptions options, string? rawKeyEnv, string? hashEnv)
    {
        Options = options;

        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();

        Absorb(hashes, sources, $"{AdminApiKeyOptions.SectionName}:{nameof(AdminApiKeyOptions.ApiKeyHashes)}", options.ApiKeyHashes, preHashed: true);
        Absorb(hashes, sources, $"{AdminApiKeyOptions.SectionName}:{nameof(AdminApiKeyOptions.ApiKeys)}", options.ApiKeys, preHashed: false);
        Absorb(hashes, sources, HashEnvVar, Split(hashEnv), preHashed: true);
        Absorb(hashes, sources, RawKeyEnvVar, Split(rawKeyEnv), preHashed: false);

        _hashes = hashes;
        Sources = sources;
    }

    public AdminApiKeyOptions Options { get; }

    /// <summary>Where the keys came from, with counts — safe to log.</summary>
    public IReadOnlyList<string> Sources { get; }

    public int Count => _hashes.Count;

    public bool IsConfigured => _hashes.Count > 0;

    /// <summary>Constant-time membership test for a presented key.</summary>
    public bool Matches(string presentedKey)
    {
        var candidate = ComputeHash(Canonicalize(presentedKey));

        // Deliberately not short-circuiting: every configured hash is compared,
        // so elapsed time cannot reveal which key matched or how many precede it.
        var matched = false;
        foreach (var known in _hashes)
            matched |= FixedTimeEqualsHex(known, candidate);

        return matched;
    }

    /// <summary>
    /// GUIDs hash in canonical lowercase "D" form, so digests generated back when
    /// keys had to be GUIDs keep working. Anything else hashes verbatim, since
    /// case carries entropy in a freely chosen key.
    /// </summary>
    public static string Canonicalize(string key)
    {
        var trimmed = key.Trim();
        return Guid.TryParse(trimmed, out var guid)
            ? guid.ToString("D").ToLowerInvariant()
            : trimmed;
    }

    public static string ComputeHash(string canonicalKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey))).ToLowerInvariant();

    private static void Absorb(
        HashSet<string> hashes, List<string> sources, string sourceName,
        IEnumerable<string>? values, bool preHashed)
    {
        if (values is null)
            return;

        var added = 0;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var trimmed = value.Trim();
            var hash = preHashed ? trimmed.ToLowerInvariant() : ComputeHash(Canonicalize(trimmed));
            if (hashes.Add(hash))
                added++;
        }

        if (added > 0)
            sources.Add($"{sourceName} ({added})");
    }

    private static IEnumerable<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool FixedTimeEqualsHex(string configuredHash, string providedHash)
    {
        if (string.IsNullOrWhiteSpace(configuredHash))
            return false;

        var configured = Encoding.UTF8.GetBytes(configuredHash.Trim().ToLowerInvariant());
        var provided = Encoding.UTF8.GetBytes(providedHash);

        return configured.Length == provided.Length &&
               CryptographicOperations.FixedTimeEquals(configured, provided);
    }
}
