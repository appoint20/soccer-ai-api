using Microsoft.IdentityModel.Tokens;

namespace SoccerAi.Api.Security;

/// <summary>
/// The public keys Supabase signs its access tokens with, cached.
/// </summary>
/// <remarks>
/// Fetched straight from the project's JWKS rather than through OIDC discovery:
/// GoTrue publishes the key set but is not a full OpenID provider, so the
/// discovery document cannot be relied on.
///
/// A failed refresh keeps serving the previous key set. Supabase being briefly
/// unreachable should not sign every user out — the keys it published a minute
/// ago are still the keys that signed the token in the caller's hand.
/// </remarks>
public sealed class SupabaseSigningKeys(string jwksUrl, SecurityKey? symmetric)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private IReadOnlyList<SecurityKey> _cached = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public IEnumerable<SecurityKey> Resolve()
    {
        var keys = Current().ToList();
        // A legacy HS256 project has no JWKS at all; its shared secret is the
        // only key, and it is always offered alongside whatever JWKS returned.
        if (symmetric is not null) keys.Add(symmetric);
        return keys;
    }

    private IReadOnlyList<SecurityKey> Current()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _fetchedAt < RefreshAfter) return _cached;

            try
            {
                var json = Http.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
                _cached = [.. new JsonWebKeySet(json).GetSigningKeys()];
                _fetchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception)
            {
                // Keep the last good set and try again on the next request
                // rather than failing every call in the meantime.
                _fetchedAt = DateTimeOffset.UtcNow.Add(-RefreshAfter).AddMinutes(1);
            }

            return _cached;
        }
    }
}
