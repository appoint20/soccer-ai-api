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
        var aiEntity = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixture.Id && a.Lang == lang, ct);

        // If AI is missing and it's a critical match (e.g., today), we could trigger it on-demand.
        // For now, we'll just check if it's there.
        
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

        if (aiEntity is { HomeProb: > 0 })
        {
            // CACHE HIT: Use stored mathematical probabilities, skip heavy ML models
            prediction = new WeightedPrediction
            {
                HomeProb = aiEntity.HomeProb,
                DrawProb = aiEntity.DrawProb,
                AwayProb = aiEntity.AwayProb,
                Over25Prob = aiEntity.Over25Prob,
                BTTSProb = aiEntity.BttsProb,
                Confidence = aiEntity.Confidence,
                MatchWinner = aiEntity.Recommendation.ToLower().Contains("home") ? "home" : 
                             aiEntity.Recommendation.ToLower().Contains("away") ? "away" : "draw"
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

        var odds = BuildMatchContext(fixture);
        var decisions = await decisionService.Evaluate(odds, stats, h2h, prediction, models, ai);

        // Persist AI decision layer results if they were just computed (not from cache)
        // This happens when the analysis existed but decision layer data was missing (legacy data)
        if (aiEntity != null && aiEntity.AiOverallConfidence == 0 && decisions.Qualification.Label?.Contains("AI Decision Layer") == true)
        {
            await PersistAiDecisionsAsync(fixture.Id, decisions, ct);
        }

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
            OddsBttsYes = odds.OddsBttsYes,
            OddsHomeWin = odds.OddsHome,
            OddsAwayWin = odds.OddsAway,
            OddsDraw = odds.OddsDraw,
            Ai = ai,
            HomeRestDays = homeRest,
            AwayRestDays = awayRest
        };
    }

    /// <summary>
    /// Persists AI Decision Layer market qualifications to the FixtureAnalysis rows
    /// for the given fixture, so subsequent API requests use the cached decisions.
    /// </summary>
    private async Task PersistAiDecisionsAsync(int fixtureId, DecisionServiceResult decisions, CancellationToken ct)
    {
        try
        {
            var analyses = await dbContext.FixtureAnalyses
                .Where(a => a.FixtureId == fixtureId && a.AiOverallConfidence == 0)
                .ToListAsync(ct);

            if (analyses.Count == 0) return;

            foreach (var analysis in analyses)
            {
                analysis.AiOver25Qualified = decisions.Markets.Over25.IsQualified;
                analysis.AiBttsQualified = decisions.Markets.BTTS.IsQualified;
                analysis.AiUnder25Qualified = decisions.Markets.LowScoring.IsQualified;
                analysis.AiGoals23Qualified = decisions.Markets.TwoToThreeGoals.IsQualified;
                analysis.AiHomeWinQualified = decisions.Markets.MatchWinner.IsQualified; 
                analysis.AiAwayWinQualified = false; // Derived from MatchWinner context
                analysis.AiBestBet = decisions.Qualification.Label ?? "";
                analysis.AiOverallConfidence = (int)(decisions.Qualification.CombinedProbability * 100);
                analysis.UpdatedAt = DateTimeOffset.UtcNow;
            }

            // Use a minimum confidence of 1 to mark as "processed" even if no market qualified
            if (analyses.Any(a => a.AiOverallConfidence == 0))
            {
                foreach (var a in analyses.Where(x => x.AiOverallConfidence == 0))
                    a.AiOverallConfidence = 1; // Mark as processed
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Non-critical — will retry on next request
        }
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

    private static double? NormalizeOdds(double? odds)
    {
        if (!odds.HasValue || odds.Value == 0) return null;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }

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
        _ => $"League {leagueId}"
    };
}
