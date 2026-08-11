using System.Globalization;

namespace SoccerAi.Tools;

/// <summary>
/// Parses <c>--name=value</c> command arguments.
///
/// Arguments are trimmed before matching. A shell line continuation typed on a
/// single line leaves a literal backslash-space, so the argument arrives as
/// <c>" --sqlite=path"</c> with a leading space. Matched strictly, that option
/// silently vanishes and the command runs against its default — which reads as
/// a missing file rather than a mistyped command, and sends you looking in the
/// wrong place entirely.
/// </summary>
public static class CommandArgs
{
    public static string? String(string[] args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);

        var prefix = $"{name}=";

        return args
            .Select(a => a.Trim().TrimStart('\\').Trim())
            .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..]
            .Trim()
            .Trim('"');
    }

    public static int? Int(string[] args, string name) =>
        int.TryParse(String(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static double? Double(string[] args, string name) =>
        double.TryParse(String(args, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static DateTimeOffset? Date(string[] args, string name) =>
        DateTimeOffset.TryParse(
            String(args, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    /// <summary>True when a bare flag such as <c>--dry-run</c> is present.</summary>
    public static bool Flag(string[] args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Any(a =>
            a.Trim().TrimStart('\\').Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
