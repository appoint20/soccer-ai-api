namespace SoccerAi.Application.Services.Evaluation;

/// <summary>
/// Recovers the RAW model-vs-market divergence from calibrated probabilities.
///
/// The calibrated probability is p_cal = (1−w)·p_DC + w·p_mkt, so
/// |p_DC − p_mkt| = |p_cal − p_mkt| / (1−w). Without this correction a
/// divergence report computed post-calibration understates edge by (1−w)
/// (halves it at the default w = 0.5).
/// </summary>
public static class CalibrationDivergence
{
    public static double RecoverModelDivergence(double pCalibrated, double pMarket, double marketWeight)
    {
        var raw = Math.Abs(pCalibrated - pMarket);
        if (marketWeight >= 0.999) return raw; // degenerate: p_cal ≈ p_mkt, nothing to recover
        return Math.Clamp(raw / (1 - marketWeight), 0, 1);
    }
}
