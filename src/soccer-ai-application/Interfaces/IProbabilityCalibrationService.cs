using System.Text.Json.Serialization;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>Raw vs isotonic-calibrated probability for one market (snapshot audit).</summary>
public sealed record CalibrationTraceEntry(
    [property: JsonPropertyName("market")] string Market,
    [property: JsonPropertyName("raw_p")] double RawP,
    [property: JsonPropertyName("calibrated_p")] double CalibratedP,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("training_samples")] int TrainingSamples);

public sealed record CalibrationResult(
    WeightedPrediction Calibrated,
    IReadOnlyList<CalibrationTraceEntry> Trace);

/// <summary>
/// Walk-forward isotonic calibration: maps model probabilities toward observed
/// frequencies using ONLY predictions+outcomes from strictly before the
/// fixture's ISO week. Below the minimum sample count it is a pass-through.
/// The EV gate and product output consume the calibrated probabilities; the
/// immutable prediction ledger stores RAW ones for calibration training;
/// mutable analysis caches must never be used as historical evidence.
/// </summary>
public interface IProbabilityCalibrationService
{
    Task<CalibrationResult> ApplyAsync(
        WeightedPrediction raw, DateTimeOffset asOf, CancellationToken ct = default, string modelVersion = "");
}
