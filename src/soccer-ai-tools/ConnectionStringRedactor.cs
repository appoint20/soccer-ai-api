using System.Text.RegularExpressions;

namespace SoccerAi.Tools;

/// <summary>
/// Removes credentials from a connection string before it is printed.
///
/// Console output ends up in screenshots, support threads and chat messages,
/// and a managed database URL carries its password inline. Printing the target
/// of a migration is genuinely useful; printing the password with it is how
/// credentials leak.
/// </summary>
public static partial class ConnectionStringRedactor
{
    public static string Redact(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

        // Key/value form: Host=...;Password=secret;...
        var redacted = KeyValuePassword().Replace(connectionString, "$1=****");

        // URL form: postgresql://user:secret@host/db
        return UrlPassword().Replace(redacted, "$1****@");
    }

    [GeneratedRegex(@"\b(password|pwd)\s*=\s*[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValuePassword();

    [GeneratedRegex(@"(://[^:/@\s]+:)[^@\s]+@")]
    private static partial Regex UrlPassword();
}
