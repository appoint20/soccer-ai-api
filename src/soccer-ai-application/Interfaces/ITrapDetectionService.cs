using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Detects dangerous match profiles: low-scoring traps, market disagreements,
/// ultra-defensive setups.
/// </summary>
public interface ITrapDetectionService
{
    TrapResult Detect(
        ProbabilityBundle bundle,
        WeightedPrediction? prediction,
        MatchContext odds,
        TeamStatsResponse? teamStats = null);
}

/// <summary>
/// Detailed trap analysis result with individual flags and progressive penalties.
/// </summary>
public sealed class TrapResult
{
    public bool LowScoreTrap { get; init; }
    public bool MarketMismatch { get; init; }
    public bool DefensiveMatch { get; init; }
    public bool RelegationTrap { get; init; }
    
    /// <summary>Points to deduct from the final 100-point feature score.</summary>
    public double PenaltyScore { get; init; }
    
    public bool IsTrap => PenaltyScore <= -15; // Hard avoid if penalty is massive
    public string Reason { get; init; } = string.Empty;

    public static TrapResult Safe => new() { Reason = string.Empty, PenaltyScore = 0 };
}
