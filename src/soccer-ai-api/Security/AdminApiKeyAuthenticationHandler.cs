using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SoccerAi.Api.Security;

public sealed class AdminApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AdminApiKeyRegistry registry)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No keys anywhere: decline rather than fail, so the other schemes in
        // CombinedPolicy still get their turn.
        if (!registry.IsConfigured)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(registry.Options.HeaderName, out var rawValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var presented = rawValues.ToString().Trim();

        if (presented.Length == 0)
            return Task.FromResult(AuthenticateResult.Fail(
                $"The {registry.Options.HeaderName} header was present but empty."));

        // Reported separately from a bad key: this one is a malformed request,
        // and saying so saves guessing at which of the two went wrong.
        if (presented.Length < registry.Options.MinimumKeyLength)
            return Task.FromResult(AuthenticateResult.Fail(
                $"API key is too short — expected at least {registry.Options.MinimumKeyLength} characters."));

        if (!registry.Matches(presented))
            return Task.FromResult(AuthenticateResult.Fail(
                $"API key was not recognised. {registry.Count} key(s) are configured."));

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
}
