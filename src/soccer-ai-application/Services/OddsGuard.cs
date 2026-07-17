namespace SoccerAi.Application.Services;

/// <summary>
/// Sanity guard for decimal odds. Historic rows may contain culture-corrupted
/// values (e.g. "1.85" parsed as 185 under a de-DE locale). Invalid odds are
/// EXCLUDED — never clamped, never substituted.
/// </summary>
public static class OddsGuard
{
    /// <summary>Plausible decimal odds range for 1X2/O-U/BTTS markets.</summary>
    public const double Min = 1.01;
    public const double Max = 15.0;

    public static bool IsValid(double? odds) => odds is >= Min and <= Max;

    /// <summary>Returns the odds when plausible, null otherwise.</summary>
    public static double? Sanitize(double? odds) => IsValid(odds) ? odds : null;
}
