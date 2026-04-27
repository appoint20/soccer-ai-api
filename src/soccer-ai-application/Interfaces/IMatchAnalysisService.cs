using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Shared match analysis pipeline — single source of truth for both
/// the analysis and combination endpoints.
/// </summary>
public interface IMatchAnalysisService
{
    /// <summary>
    /// Run full analysis pipeline for a single fixture:
    /// Team stats → Poisson → Monte Carlo → ML → Weighted prediction → Decisions
    /// </summary>
    Task<FixtureAnalysisResult> AnalyzeFixtureAsync(Fixture fixture, string lang, bool refresh = false, CancellationToken ct = default);


}

/// <summary>
/// Complete analysis result consumed by both handlers. 
/// Contains everything needed to build API responses or combination candidates.
/// </summary>
public sealed class FixtureAnalysisResult
{
    public required int FixtureId { get; init; }
    public required TeamStatsResponse TeamStats { get; init; }
    public required StatisticalModels Models { get; init; }
    public required HeadToHeadModel H2H { get; init; }
    public WeightedPrediction? Prediction { get; init; }
    public required DecisionServiceResult Decisions { get; init; }
    public required string LeagueName { get; init; }
    public AiAnalysisDto? Ai { get; init; }
    public double? OddsOver25 { get; init; }
    public double? OddsBttsYes { get; init; }
    public double? OddsHomeWin { get; init; }
    public double? OddsAwayWin { get; init; }
    public double? OddsDraw { get; init; }
    public float? HomeRestDays { get; init; }
    public float? AwayRestDays { get; init; }
}
