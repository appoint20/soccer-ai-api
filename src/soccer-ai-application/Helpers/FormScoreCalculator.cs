namespace SoccerAi.Application.Helpers;

/// <summary>
/// Calculates and formats team form metrics.
/// Eliminates duplicate form calculation logic across handlers.
/// </summary>
public static class FormScoreCalculator
{
    /// <summary>
    /// Calculates win percentage from form string (e.g., "WWDLW").
    /// </summary>
    public static int CalculateFormPercentage(string? form)
    {
        if (string.IsNullOrWhiteSpace(form))
            return 0;

        int points = 0;
        foreach (var c in form.ToUpperInvariant())
        {
            if (c == 'W') points += 3;
            else if (c == 'D') points += 1;
        }

        var maxPoints = form.Length * 3;
        return maxPoints > 0 ? (int)Math.Round((points / (double)maxPoints) * 100) : 0;
    }

    /// <summary>
    /// Gets human-readable form description based on percentage.
    /// </summary>
    public static string GetFormDescription(int percentage)
    {
        return percentage switch
        {
            >= 80 => "Excellent",
            >= 60 => "Good",
            >= 40 => "Fair",
            >= 20 => "Poor",
            _ => "Critical"
        };
    }
}
