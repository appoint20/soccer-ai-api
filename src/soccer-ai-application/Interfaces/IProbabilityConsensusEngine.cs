using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Combines outputs from all probability models into a single weighted prediction.
/// Consensus has the last word — all weighting logic lives here.
/// </summary>
public interface IProbabilityConsensusEngine
{
    WeightedPrediction? Combine(ProbabilityBundle bundle, TeamStatsResponse stats, int leagueId, HeadToHeadModel? h2h, string? geminiRecommendation, double? geminiConfidence);
}

