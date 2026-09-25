namespace SoccerAi.Application.Models;

/// <summary>
/// The provider's own read of a fixture: outcome percentages, a side-by-side
/// comparison and each team's recent scoring, none of which involve a price.
/// </summary>
/// <remarks>
/// A second opinion, never an authority. Our model owns the probabilities the
/// gate reads; this is evidence to agree or disagree with, and the material the
/// match panel explains in words — "Dortmund 63% form against Bremen's 37%"
/// says something a reader can act on, where a bare probability does not.
/// </remarks>
public sealed record ProviderPrediction
{
    /// <summary>Outcome chances as the provider sees them, 0–1.</summary>
    public double? PercentHome { get; init; }

    public double? PercentDraw { get; init; }

    public double? PercentAway { get; init; }

    /// <summary>
    /// The home team's share of each comparison, 0–1; the away side is the
    /// remainder. Provider fields: form, att, def, poisson_distribution, h2h,
    /// goals, total.
    /// </summary>
    public double? Form { get; init; }

    public double? Attack { get; init; }

    public double? Defence { get; init; }

    public double? Poisson { get; init; }

    public double? HeadToHead { get; init; }

    public double? Goals { get; init; }

    public double? Total { get; init; }

    /// <summary>The provider's plain-language call, e.g. "Double chance : Bremen or draw".</summary>
    public string? Advice { get; init; }

    /// <summary>Its named winner, when it names one.</summary>
    public string? WinnerName { get; init; }

    /// <summary>Its goals line, e.g. "-3.5", or null when it offers none.</summary>
    public string? UnderOver { get; init; }

    public TeamRecentForm? Home { get; init; }

    public TeamRecentForm? Away { get; init; }
}

/// <summary>One side's last five matches, as the provider summarises them.</summary>
public sealed record TeamRecentForm
{
    public int Played { get; init; }

    /// <summary>Form, attack and defence ratings, 0–1.</summary>
    public double? Form { get; init; }

    public double? Attack { get; init; }

    public double? Defence { get; init; }

    /// <summary>Goals scored and conceded per match across those fixtures.</summary>
    public double? GoalsForAverage { get; init; }

    public double? GoalsAgainstAverage { get; init; }
}
