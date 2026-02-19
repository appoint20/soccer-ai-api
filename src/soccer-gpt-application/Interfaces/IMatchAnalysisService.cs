using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

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
    Task<FixtureAnalysis> AnalyzeFixtureAsync(Fixture fixture, CancellationToken ct);
}

/// <summary>
/// Complete analysis result consumed by both handlers. 
/// Contains everything needed to build API responses or combination candidates.
/// </summary>
public sealed class FixtureAnalysis
{
    public required TeamStatsResponse TeamStats { get; init; }
    public required StatisticalModels Models { get; init; }
    public required HeadToHeadModel H2H { get; init; }
    public WeightedPrediction? Prediction { get; init; }
    public required DecisionServiceResult Decisions { get; init; }
    public required string LeagueName { get; init; }

    /// <summary>Fixture-level odds, already normalized.</summary>
    public double OddsOver25 { get; init; }
    public double OddsBttsYes { get; init; }
    public double OddsHomeWin { get; init; }
    public double OddsAwayWin { get; init; }
    public double OddsDraw { get; init; }
}
