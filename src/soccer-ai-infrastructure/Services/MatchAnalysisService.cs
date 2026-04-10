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
    public async Task<FixtureAnalysisResult> AnalyzeFixtureAsync(Fixture fixture, string lang, CancellationToken ct)
    {
        var geminiEntity = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixture.Id && a.Lang == lang, ct);

        WeightedPrediction? prediction;
        StatisticalModels models;
        TeamStatsResponse? stats = null;
        HeadToHeadModel? h2h = null;
        float? homeRest = null;
        float? awayRest = null;

        // 2. ALWAYS load data (team stats + H2H) for UI visibility
        var data = await dataProvider.LoadAsync(fixture, ct);
        stats = data.TeamStats;
        h2h = data.H2H;
        homeRest = data.HomeRestDays;
        awayRest = data.AwayRestDays;

        if (geminiEntity != null && geminiEntity.HomeProb > 0)
        {
            // CACHE HIT: Use stored mathematical probabilities, skip heavy ML models
            prediction = new WeightedPrediction
            {
                HomeProb = geminiEntity.HomeProb,
                DrawProb = geminiEntity.DrawProb,
                AwayProb = geminiEntity.AwayProb,
                Over25Prob = geminiEntity.Over25Prob,
                BTTSProb = geminiEntity.BttsProb,
                Confidence = geminiEntity.Confidence,
                MatchWinner = geminiEntity.Recommendation.ToLower().Contains("home") ? "home" : 
                             geminiEntity.Recommendation.ToLower().Contains("away") ? "away" : "draw"
            };
            
            models = new StatisticalModels(); 
        }
        else
        {
            // CACHE MISS: Run the heavy mathematical engines (Poisson → Monte Carlo → ML)
            var bundle = await pipeline.RunAsync(fixture, stats, ct);
            prediction = consensus.Combine(bundle, stats, fixture.LeagueId, h2h, null, null);
            
            models = new StatisticalModels
            {
                Poisson = bundle.Poisson,
                MonteCarlo = bundle.MonteCarlo
            };
        }

        var odds = BuildMatchContext(fixture);
        var decisions = decisionService.Evaluate(odds, stats, h2h, prediction, models);

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
        return new FixtureAnalysisResult
        {
            FixtureId = fixture.Id,
            TeamStats = stats ?? new TeamStatsResponse(),
            Models = models,
            H2H = h2h ?? new HeadToHeadModel(),
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
            HomeRestDays = homeRest,
            AwayRestDays = awayRest
        };
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
        46 => "National League",
        78 => "Bundesliga",
        79 => "2. Bundesliga",
        80 => "3. Liga",
        135 => "Serie A",
        136 => "Serie B",
        140 => "La Liga",
        141 => "La Liga 2",
        61 => "Ligue 1",
        62 => "Ligue 2",
        2 => "Champions League",
        3 => "Europa League",
        _ => $"League {leagueId}"
    };
}
