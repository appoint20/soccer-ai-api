using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Orchestrator-only analysis pipeline for a single fixture.
/// Used by both the analysis and combination endpoints — single source of truth.
///
/// Pipeline: MatchDataProvider → ProbabilityPipeline → ConsensusEngine → DecisionService
/// </summary>
public sealed class MatchAnalysisService(
    IMatchDataProvider dataProvider,
    IProbabilityPipeline pipeline,
    IProbabilityConsensusEngine consensus,
    IDecisionService decisionService,
    ILogger<MatchAnalysisService> logger) : IMatchAnalysisService
{
    public async Task<FixtureAnalysis> AnalyzeFixtureAsync(Fixture fixture, CancellationToken ct)
    {
        // 1. Load data (team stats + H2H)
        var data = await dataProvider.LoadAsync(fixture, ct);

        // 2. Run models (Poisson → Monte Carlo → ML)
        var bundle = await pipeline.RunAsync(fixture, data.TeamStats, ct);

        // 3. Consensus — weighted combination of all models + league volatility
        var prediction = consensus.Combine(bundle, data.TeamStats, fixture.LeagueId);

        // 4. Decision layer
        var odds = BuildMatchContext(fixture);
        var models = new StatisticalModels
        {
            Poisson = bundle.Poisson,
            MonteCarlo = bundle.MonteCarlo
        };

        var decisions = decisionService.Evaluate(odds, data.TeamStats, data.H2H, prediction, models);

        // Return
        return new FixtureAnalysis
        {
            TeamStats = data.TeamStats,
            Models = models,
            H2H = data.H2H,
            Prediction = prediction,
            Decisions = decisions,
            LeagueName = GetLeagueName(fixture.LeagueId),
            OddsOver25 = odds.OddsOver25,
            OddsBttsYes = odds.OddsBttsYes,
            OddsHomeWin = odds.OddsHomeWin,
            OddsAwayWin = odds.OddsAwayWin,
            OddsDraw = odds.OddsDraw
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static MatchContext BuildMatchContext(Fixture fixture) => new()
    {
        Date = fixture.Date,
        OddsOver25 = NormalizeOdds(fixture.Over25Odds),
        OddsBttsYes = NormalizeOdds(fixture.BttsYesOdds),
        OddsHomeWin = NormalizeOdds(fixture.HomeWinOdds),
        OddsAwayWin = NormalizeOdds(fixture.AwayWinOdds),
        OddsDraw = NormalizeOdds(fixture.DrawOdds)
    };

    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }

    private static string GetLeagueName(int leagueId) => leagueId switch
    {
        39 => "Premier League",
        40 => "Championship",
        41 => "League One",
        42 => "League Two",
        78 => "Bundesliga",
        79 => "2. Bundesliga",
        135 => "Serie A",
        136 => "Serie B",
        140 => "La Liga",
        141 => "La Liga 2",
        61 => "Ligue 1",
        62 => "Ligue 2",
        _ => $"League {leagueId}"
    };
}
