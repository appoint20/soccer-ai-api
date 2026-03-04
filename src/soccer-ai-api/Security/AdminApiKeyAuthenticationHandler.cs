using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SoccerAi.Api.Configuration;

namespace SoccerAi.Api.Security;

public sealed class AdminApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<AdminApiKeyOptions> adminOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = adminOptions.Value;
        var configuredHashes = options.ApiKeyHashes;
        if (configuredHashes.Length == 0)
        {
            var fallbackHash = Environment.GetEnvironmentVariable("ADMIN_API_KEY_HASH");
            if (!string.IsNullOrWhiteSpace(fallbackHash))
            {
                configuredHashes = [fallbackHash];
            }
        }

        if (configuredHashes.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(options.HeaderName, out var rawValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var raw = rawValues.ToString();
        if (!Guid.TryParse(raw, out var providedGuid))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key format."));
        }

        // Canonical form for deterministic hashing.
        var providedCanonical = providedGuid.ToString("D").ToLowerInvariant();
        var providedHash = ComputeSha256Hex(providedCanonical);

        var isValid = configuredHashes.Any(hash => FixedTimeEqualsHex(hash, providedHash));
        if (!isValid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Name, "AdminApiKey")
        };

        var identity = new ClaimsIdentity(claims, AdminApiKeyAuthenticationDefaults.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AdminApiKeyAuthenticationDefaults.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

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
