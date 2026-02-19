using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Detects dangerous match profiles: low-scoring traps, market disagreements,
/// ultra-defensive setups.
/// </summary>
public interface ITrapDetectionService
{
    TrapResult Detect(
        ProbabilityBundle bundle,
        WeightedPrediction? prediction,
        MatchContext odds);
}

/// <summary>
/// Detailed trap analysis result with individual flags.
/// </summary>
public sealed class TrapResult
{
    public bool LowScoreTrap { get; init; }
    public bool MarketMismatch { get; init; }
    public bool DefensiveMatch { get; init; }
    public bool IsTrap => LowScoreTrap || MarketMismatch || DefensiveMatch;
    public string Reason { get; init; } = string.Empty;

    public static TrapResult Safe => new() { Reason = string.Empty };
}
