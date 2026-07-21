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

    // ── Walk-forward isotonic calibration layer (v5) ──

    /// <summary>Master switch for the isotonic layer.</summary>
    public bool IsotonicEnabled { get; set; } = true;

    /// <summary>
    /// Minimum (prediction, outcome) pairs per market before the isotonic map
    /// activates; below this the layer is a pass-through.
    /// </summary>
    public int IsotonicMinSamples { get; set; } = 300;
}
