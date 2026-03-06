using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

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
    IApplicationDbContext dbContext) : IMatchAnalysisService
{
    public async Task<List<FixtureAnalysisResult>> AnalyzeLatestFixtureByAsync(DateTime now)
    {
        var startUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddDays(5);

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc)
            .ToListAsync();

        var results = new List<FixtureAnalysisResult>();
        foreach (var fixture in fixtures)
        {
            try
            {
                var result = await AnalyzeFixtureAsync(fixture, "en", CancellationToken.None);
                results.Add(result);
            }
            catch (Exception ex)
            {
                // Log and continue with other fixtures
                Console.WriteLine($"Error analyzing fixture {fixture.Id}: {ex.Message}");
            }
        }

        return results;
    }

    public async Task<FixtureAnalysisResult> AnalyzeFixtureAsync(Fixture fixture, string lang, CancellationToken ct)
    {
        // 1. Load data (team stats + H2H)
        var data = await dataProvider.LoadAsync(fixture, ct);

        // 2. Run models (Poisson → Monte Carlo → ML)
        var bundle = await pipeline.RunAsync(fixture, data.TeamStats, ct);

        // 3. Consensus — weighted combination of all models + league volatility + H2H divergence + momentum
        var prediction = consensus.Combine(bundle, data.TeamStats, fixture.LeagueId, data.H2H, null, null);

        // 4. Decision layer
        var odds = BuildMatchContext(fixture);
        var models = new StatisticalModels
        {
            Poisson = bundle.Poisson,
            MonteCarlo = bundle.MonteCarlo
        };

        var decisions = decisionService.Evaluate(odds, data.TeamStats, data.H2H, prediction, models);

        var geminiEntity = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixture.Id && a.Lang == lang, ct);

        var gemini = geminiEntity != null ? new GeminiAnalysis
        {
            Recommendation = geminiEntity.Recommendation ?? "Avoid",
            Confidence = geminiEntity.Confidence,
            Reasoning = geminiEntity.PredictionReason ?? "",
            Analysis = geminiEntity.Analysis ?? "",
            IsTrap = geminiEntity.TrapDetected,
            TrapReason = geminiEntity.TrapReason ?? "",
            OneLineSummary = geminiEntity.ConsensusEvaluation ?? "",
            BttsSummary = geminiEntity.BttsSummary ?? "",
            Over25Summary = geminiEntity.Over25Summary ?? "",
            Under25Summary = geminiEntity.Under25Summary ?? "",
            HomeWinSummary = geminiEntity.HomeWinSummary ?? "",
            AwayWinSummary = geminiEntity.AwayWinSummary ?? ""
        } : new GeminiAnalysis();

        // Build result
        var analysisResult = new FixtureAnalysisResult
        {
            FixtureId = fixture.Id,
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
            OddsDraw = odds.OddsDraw,
            Gemini = gemini,
            HomeElo = fixture.HomeElo,
            AwayElo = fixture.AwayElo,
            HomeRestDays = data.HomeRestDays,
            AwayRestDays = data.AwayRestDays
        };



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
