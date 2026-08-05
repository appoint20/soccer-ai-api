using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Orchestrator-only analysis pipeline for a single fixture.
/// Used by both the analysis and combination endpoints — single source of truth.
///
/// Pipeline: MatchDataProvider → ProbabilityPipeline (DC → calibration) → DecisionService
/// </summary>
public sealed class MatchAnalysisService(
    IMatchDataProvider dataProvider,
    IProbabilityPipeline pipeline,
    IDecisionService decisionService,
    IStrategicSignalService signalService,
    IProbabilityCalibrationService calibrationService,
    IApplicationDbContext dbContext) : IMatchAnalysisService
{
    public async Task<FixtureAnalysisResult> AnalyzeFixtureAsync(Fixture fixture, string lang, bool refresh = false, CancellationToken ct = default)
    {
        // Always load the persisted analysis row: it carries the AI narrative.
        // `refresh` only controls whether the MATH is recomputed.
        var aiEntity = await dbContext.FixtureAnalyses
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

        // Math cache is only trusted when COMPLETE. Legacy rows have
        // DrawProb hard-zeroed and no Goals23Prob — using them saturates
        // 1X2 and goals_2_3 log loss with fake 0-probabilities.
        var cacheComplete = aiEntity is { HomeProb: > 0, DrawProb: > 0, Goals23Prob: > 0 };

        if (!refresh && cacheComplete)
        {
            // CACHE HIT: Use stored mathematical probabilities, skip the models
            prediction = new WeightedPrediction
            {
                HomeProb = aiEntity!.HomeProb,
                DrawProb = aiEntity.DrawProb,
                AwayProb = aiEntity.AwayProb,
                Over25Prob = aiEntity.Over25Prob,
                BTTSProb = aiEntity.BttsProb,
                TwoToThreeGoalsProb = aiEntity.Goals23Prob,
                Confidence = aiEntity.Confidence,
                MatchWinner = aiEntity.Recommendation.ToLower().Contains("home") ? "home" :
                             aiEntity.Recommendation.ToLower().Contains("away") ? "away" : "draw"
            };

            models = new StatisticalModels();
        }
        else
        {
            // CACHE MISS: Run the single probability flow (Dixon-Coles → calibration)
            var bundle = await pipeline.RunAsync(fixture, stats, ct);
            prediction = bundle != null
                ? WeightedPrediction.FromCalibrated(bundle.Calibrated)
                : null;

            models = bundle != null
                ? new StatisticalModels { Poisson = bundle.Poisson }
                : new StatisticalModels();
        }

        var ai = aiEntity != null ? new AiAnalysisDto
        {
            Recommendation = aiEntity.Recommendation ?? "Avoid",
            Confidence = aiEntity.Confidence,
            Reasoning = aiEntity.PredictionReason ?? "",
            Analysis = aiEntity.Analysis ?? "",
            IsTrap = aiEntity.TrapDetected,
            TrapReason = aiEntity.TrapReason ?? "",
            OneLineSummary = aiEntity.ConsensusEvaluation ?? "",
            BttsSummary = aiEntity.BttsSummary ?? "",
            Over25Summary = aiEntity.Over25Summary ?? "",
            Under25Summary = aiEntity.Under25Summary ?? "",
            HomeWinSummary = aiEntity.HomeWinSummary ?? "",
            AwayWinSummary = aiEntity.AwayWinSummary ?? "",
            // AI Decision Layer per-market qualifications
            AiOver25Qualified = aiEntity.AiOver25Qualified,
            AiBttsQualified = aiEntity.AiBttsQualified,
            AiUnder25Qualified = aiEntity.AiUnder25Qualified,
            AiGoals23Qualified = aiEntity.AiGoals23Qualified,
            AiHomeWinQualified = aiEntity.AiHomeWinQualified,
            AiAwayWinQualified = aiEntity.AiAwayWinQualified,
            AiBestBet = aiEntity.AiBestBet ?? "",
            AiOverallConfidence = aiEntity.AiOverallConfidence
        } : new AiAnalysisDto();

        // Walk-forward isotonic calibration: RAW probabilities stay in the math
        // cache (training data); decisions and product output use calibrated.
        var rawPrediction = prediction;
        IReadOnlyList<CalibrationTraceEntry>? calibrationTrace = null;
        if (prediction != null)
        {
            var calibration = await calibrationService.ApplyAsync(prediction, fixture.Date, ct);
            prediction = calibration.Calibrated;
            calibrationTrace = calibration.Trace;
        }

        // Strategic signal catalog (facts; DC model passed for divergence signals
        // when it ran this request — cached-math path degrades those gracefully).
        var signals = await signalService.ComputeAsync(
            fixture, models.Poisson.IsValid ? models.Poisson : null, ct);

        // Confluence decision layer: calibrated probabilities + signals.
        var odds = BuildMatchContext(fixture, homeRest, awayRest);
        var decisions = await decisionService.Evaluate(odds, stats, h2h, prediction, models, signals, ai);

        // Build result
        return new FixtureAnalysisResult
        {
            FixtureId = fixture.Id,
            TeamStats = stats,
            Models = models,
            H2H = h2h,
            Prediction = prediction,
            Decisions = decisions,
            LeagueName = GetLeagueName(fixture.LeagueId),
            OddsOver25 = odds.OddsOver25,
            OddsUnder25 = odds.OddsUnder25,
            OddsBttsYes = odds.OddsBttsYes,
            OddsHomeWin = odds.OddsHome,
            OddsAwayWin = odds.OddsAway,
            OddsDraw = odds.OddsDraw,
            Ai = ai,
            HomeRestDays = homeRest,
            AwayRestDays = awayRest,
            Signals = signals,
            RawPrediction = rawPrediction,
            CalibrationTrace = calibrationTrace
        };
    }



    // ── Helpers ───────────────────────────────────────────────────

    private static MatchContext BuildMatchContext(Fixture fixture, float? homeRest = null, float? awayRest = null) => new()
    {
        Date = fixture.Date,
        LeagueId = fixture.LeagueId,
        // Sanity-guarded raw odds. Corrupted values (locale bug) are surfaced
        // as null — never rescaled: EV math must only ever see real prices.
        OddsOver25 = OddsGuard.Sanitize(fixture.Over25Odds),
        OddsUnder25 = OddsGuard.Sanitize(fixture.Under25Odds),
        OddsBttsYes = OddsGuard.Sanitize(fixture.BttsYesOdds),
        OddsHome = OddsGuard.Sanitize(fixture.HomeWinOdds),
        OddsAway = OddsGuard.Sanitize(fixture.AwayWinOdds),
        OddsDraw = OddsGuard.Sanitize(fixture.DrawOdds),
        LeagueName = GetLeagueName(fixture.LeagueId),
        HomeRestDays = homeRest,
        AwayRestDays = awayRest
    };

    private static string GetLeagueName(int leagueId) => leagueId switch
    {
        39 => "Premier League",
        40 => "Championship",
        41 => "League One",
        42 => "League Two",
        46 => "National League",
        34 => "National League",
        154 => "National League",
        43 => "National League",
        5 => "National League",
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
        848 => "Conference League",
        _ => $"League {leagueId}"
    };
}
