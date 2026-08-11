namespace SoccerAi.Infrastructure.Persistence;

/// <summary>
/// Converts a managed-platform Postgres URL into the keyword syntax Npgsql
/// accepts.
///
/// Render, Heroku, Fly and friends all hand out
/// <c>postgresql://user:pass@host:port/db</c>. Npgsql rejects that outright
/// with "Format of the initialization string does not conform to
/// specification starting at index 0" — a message that names neither Postgres
/// nor the URL, so it reads like a bug in the caller.
///
/// This lives here, next to the contexts, so every path that opens a Postgres
/// connection shares it. It previously sat private inside the DI registration,
/// which meant the one-time data migration bypassed it and failed on exactly
/// the connection string the deployment used successfully.
/// </summary>
public static class PostgresConnectionString
{
    /// <summary>
    /// Keyword strings pass through untouched, so this is always safe to apply.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);

        // Credentials are percent-encoded in a URL; Npgsql wants them raw.
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;

        var result = $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password}";

        // Managed Postgres almost always requires TLS; a local one usually
        // has none configured at all.
        if (!uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            result += ";SSL Mode=Require";

        return result;
    }
}
