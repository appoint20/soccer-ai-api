namespace SoccerAi.Application.Options;

/// <summary>
/// All Dixon-Coles model constants. Bound from the "DixonColes" configuration
/// section; defaults below are used when the section is absent.
/// No magic numbers may live in the model code itself.
/// </summary>
public sealed class DixonColesOptions
{
    public const string SectionName = "DixonColes";

    /// <summary>Score matrix is (MaxGoals+1)×(MaxGoals+1).</summary>
    public int MaxGoals { get; set; } = 8;

    /// <summary>Dixon-Coles low-score dependence parameter ρ (1997 paper).</summary>
    public double Rho { get; set; } = -0.13;

    /// <summary>
    /// Exponential time decay half-life in days. A match this old carries half
    /// the weight of a match played today. Replaces the IsCurrentSeason hard cut.
    /// </summary>
    public double DecayHalfLifeDays { get; set; } = 180;

    /// <summary>Bayesian shrinkage prior strength (in effective matches).</summary>
    public double BayesianPriorStrength { get; set; } = 10;

    /// <summary>Venue-specific weight in venue/overall blending (rest is overall).</summary>
    public double VenueWeight { get; set; } = 0.70;

    /// <summary>Minimum finished league matches before the model produces output.</summary>
    public int MinLeagueMatches { get; set; } = 10;

    /// <summary>Minimum finished matches per team before the model produces output.</summary>
    public int MinTeamMatches { get; set; } = 3;

    /// <summary>
    /// Weight applied to a team's matches played in a DIFFERENT competition,
    /// used only when its record in this division is shorter than
    /// <see cref="MinTeamMatches"/>.
    /// </summary>
    /// <remarks>
    /// Goals from another division are converted into this division's scoring
    /// environment before they are counted, but opponent quality is not
    /// corrected for — a goals model cannot see it. The discount is what keeps
    /// a promoted side's record in the tier below from being read as if it had
    /// been earned here. 1.0 would treat the two as interchangeable; 0 would
    /// restore the old behaviour of pricing such a club from nothing at all.
    /// </remarks>
    public double CrossLeagueWeight { get; set; } = 0.75;

    /// <summary>Lower clamp for expected goals λ.</summary>
    public double LambdaMin { get; set; } = 0.2;

    /// <summary>Upper clamp for expected goals λ.</summary>
    public double LambdaMax { get; set; } = 4.5;

    /// <summary>Lower clamp for league goal averages (guards divide-by-zero).</summary>
    public double MinLeagueGoalAverage { get; set; } = 0.1;
}
