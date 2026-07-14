namespace SoccerAi.Application.Options;

/// <summary>
/// Market calibration constants. Bound from the "Calibration" configuration
/// section; defaults apply when the section is absent.
/// </summary>
public sealed class CalibrationOptions
{
    public const string SectionName = "Calibration";

    /// <summary>
    /// Weight of the Shin-margin-removed market probability in the final blend:
    /// final_p = (1 − MarketWeight) × p_DC + MarketWeight × p_market.
    /// Markets without odds fall back to the pure model probability.
    /// </summary>
    public double MarketWeight { get; set; } = 0.5;
}
