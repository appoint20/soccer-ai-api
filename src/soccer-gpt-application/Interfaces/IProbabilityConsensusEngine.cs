using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Combines outputs from all probability models into a single weighted prediction.
/// Consensus has the last word — all weighting logic lives here.
/// </summary>
public interface IProbabilityConsensusEngine
{
    WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats);
    WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats, int leagueId);
}
