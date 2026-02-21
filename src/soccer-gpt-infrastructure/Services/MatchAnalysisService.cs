using Microsoft.Extensions.Caching.Memory;
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
    IMemoryCache cache,
    ILogger<MatchAnalysisService> logger) : IMatchAnalysisService
{
    public async Task<FixtureAnalysis> AnalyzeFixtureAsync(Fixture fixture, CancellationToken ct)
    {
        // Cache key includes UpdatedAt and Status so odds/score updates break the cache correctly
        var cacheKey = $"fixture_analysis_{fixture.Id}_{fixture.Status}_{fixture.UpdatedAt?.Ticks ?? 0}";

        if (cache.TryGetValue(cacheKey, out FixtureAnalysis? cachedAnalysis) && cachedAnalysis != null)
        {
            return cachedAnalysis;
        }
        // 1. Load data (team stats + H2H)
        var data = await dataProvider.LoadAsync(fixture, ct);

        // 2. Run models (Poisson → Monte Carlo → ML)
        var bundle = await pipeline.RunAsync(fixture, data.TeamStats, ct);

        // 3. Consensus — weighted combination of all models + league volatility + H2H divergence + momentum
        var prediction = consensus.Combine(bundle, data.TeamStats, fixture.LeagueId, data.H2H);

        // 4. Decision layer
        var odds = BuildMatchContext(fixture);
        var models = new StatisticalModels
        {
            Poisson = bundle.Poisson,
            MonteCarlo = bundle.MonteCarlo
        };

        var decisions = decisionService.Evaluate(odds, data.TeamStats, data.H2H, prediction, models);

        // Build result
        var analysisResult = new FixtureAnalysis
        {
            TeamStats = data.TeamStats,
            Models = models,
            H2H = data.H2H,
            Prediction = prediction,
            Decisions = decisions,
            LeagueName = GetLeagueName(fixture.LeagueId),
            OddsOver25 = odds.OddsOver25,
            OddsBttsYes = odds.OddsBttsYes,
            OddsHomeWin = odds.OddsHome,
            OddsAwayWin = odds.OddsAway,
            OddsDraw = odds.OddsDraw
        };

        // Cache for 12 hours since it automatically breaks if the fixture row updates (odds/status changes)
        cache.Set(cacheKey, analysisResult, TimeSpan.FromHours(12));

        return analysisResult;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static MatchContext BuildMatchContext(Fixture fixture) => new()
    {
        Date = fixture.Date,
        OddsOver25 = NormalizeOdds(fixture.Over25Odds),
        OddsBttsYes = NormalizeOdds(fixture.BttsYesOdds),
        OddsHome = NormalizeOdds(fixture.HomeWinOdds),
        OddsAway = NormalizeOdds(fixture.AwayWinOdds),
        OddsDraw = NormalizeOdds(fixture.DrawOdds),
        LeagueName = GetLeagueName(fixture.LeagueId)
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
