namespace SoccerAi.Application.Options;

/// <summary>
/// Supabase Auth configuration ("Supabase" section).
/// </summary>
/// <remarks>
/// Identity moves to Supabase, but this API keeps validating tokens itself —
/// it never calls Supabase on the request path. A Supabase access token is an
/// ordinary JWT: this project only has to know the issuer, the audience and
/// which keys are allowed to have signed it.
///
/// Two signing arrangements exist and both are supported, because which one a
/// project uses depends on when it was created:
/// <list type="bullet">
/// <item>Newer projects sign asymmetrically and publish the public keys at
/// <c>/auth/v1/.well-known/jwks.json</c>. Nothing secret is configured here.</item>
/// <item>Older projects sign HS256 with the project's JWT secret, which then has
/// to be supplied via <see cref="JwtSecret"/>. That value is as sensitive as a
/// password: it can mint tokens for any user.</item>
/// </list>
/// </remarks>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Project URL, e.g. https://abcdefgh.supabase.co — no trailing slash.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Legacy HS256 project JWT secret. Leave empty on JWKS projects.</summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// The service_role key, used ONLY for admin calls this API makes itself
    /// (deleting an account). Never sent to a client.
    /// </summary>
    public string ServiceRoleKey { get; set; } = string.Empty;

    /// <summary>GoTrue stamps every signed-in user's token with this audience.</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>True once a project URL is set; the scheme is not registered otherwise.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);

    /// <summary>The `iss` GoTrue puts in its tokens.</summary>
    public string Issuer => $"{Url.TrimEnd('/')}/auth/v1";

    public string JwksUrl => $"{Issuer}/.well-known/jwks.json";

    public string AdminUsersUrl => $"{Issuer}/admin/users";

    public string TokenUrl => $"{Issuer}/token?grant_type=password";
}
