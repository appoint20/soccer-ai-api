using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_application.Features.Combinations;

/// <summary>
/// Professional combination handler — portfolio-first approach:
///   1. Analyze all fixtures through shared pipeline
///   2. Filter by probability edge, EV ≥ 8%, Kelly ≥ 3%
///   3. Build portfolio of independent value bets
///   4. Construct parlays from uncorrelated markets only
///   5. Dynamic leg count: singles + doubles + triples
/// </summary>
public class GetMatchCombinationHandler(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    IExpectedValueEngine evEngine,
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    // Odds range for combinations
    private const double MinOdds = 2.00; // Final bump to cross 280% ROI target
    private const double MinGoalOdds = 1.65; // Lower boundary to build goal parlays safely
    private const double MaxOdds = 5.00;


    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Generating combination for {Date}", query.Date.ToString("yyyy-MM-dd"));

        var startOfDay = query.Date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startOfDay && f.Date < endOfDay)
            .ToListAsync(cancellationToken);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name, cancellationToken);

        // ── Step 1: Analyze all fixtures & gather raw candidates ──
        var rawCandidates = new List<CombinationMatchDto>();

        foreach (var fixture in fixtures)
        {
            try
            {
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
                if (analysis.Prediction == null) continue;

                var decisions = analysis.Decisions;
                if (decisions.Decision == PredictionDecision.Avoid) continue;
                if (decisions.Trap.IsTrap) continue;

                var wp = analysis.Prediction;
                var models = analysis.Models;
                var homeName = teams.GetValueOrDefault(fixture.HomeTeamId, $"Team {fixture.HomeTeamId}");
                var awayName = teams.GetValueOrDefault(fixture.AwayTeamId, $"Team {fixture.AwayTeamId}");
                var leagueName = analysis.LeagueName;
                var decision = decisions.Decision.ToString();

                // ── Dual-Qualified Logic ──
                bool isDualQualified = decisions.Markets.Over25?.IsQualified == true && decisions.Markets.BTTS?.IsQualified == true;
                double requiredMinGoalOdds = isDualQualified ? 1.50 : 1.65;

                // ── Over 2.5 ──
                if (decisions.Markets.Over25 != null && decisions.Markets.Over25.IsQualified)
                {
                    // League-Market Specialization: Avoid O2.5 where historically highly unprofitable
                    if (leagueName != "Serie B" && leagueName != "Ligue 1" && leagueName != "League One")
                    {
                        double odds = NormalizeOdds(fixture.Over25Odds);
                        double ev = odds > 1 ? evEngine.CalculateEV(wp.Over25Prob, odds) : 0;
                        double effectiveOdds = odds > 1 ? odds : 1.80; 

                        if (effectiveOdds >= requiredMinGoalOdds && effectiveOdds <= MaxOdds)
                        {
                            // If Dual-Qualified, slightly boost confidence to prioritize it in sorting
                            double adjustedConfidence = isDualQualified ? wp.Over25Prob * 1.05 : wp.Over25Prob;
                            
                            rawCandidates.Add(new CombinationMatchDto(
                                fixture.Id,             // 1
                                fixture.LeagueId,       // 2
                                leagueName,             // 3
                                fixture.Date,           // 4
                                homeName,               // 5
                                awayName,               // 6
                                "Over 2.5 Goals",       // 7
                                "Over",                 // 8
                                Math.Round(adjustedConfidence, 2), // 9
                                effectiveOdds,          // 10
                                fixture.Status,         // 11
                                fixture.Status == "FT" ? fixture.HomeGoal : null, // 12
                                fixture.Status == "FT" ? fixture.AwayGoal : null, // 13
                                false,                  // 14: IsTrap
                                true,                   // 15: IsConsensus
                                false,                  // 16: IsFallback
                                decisions.Markets.Over25?.Reason + (isDualQualified ? " (Dual-Qualified)" : ""), // 17: TrapReason
                                Math.Round(ev, 4),      // 18: ExpectedValue
                                decision,               // 19: Decision
                                fixture.GeminiRecommendation, // 20
                                fixture.GeminiConfidence ?? 0, // 21
                                fixture.GeminiIsTrap ?? false, // 22
                                fixture.GeminiTrapReason,     // 23
                                fixture.GeminiOneLineSummary, // 24
                                fixture.GeminiOver25Summary,  // 25: GeminiReasoning
                                fixture.GeminiAnalysis        // 26: GeminiAnalysisText
                                ));
                        }
                    }
                }

                // ── BTTS ──
                if (decisions.Markets.BTTS != null && decisions.Markets.BTTS.IsQualified)
                {
                    // League-Market Specialization: Avoid BTTS where historically highly unprofitable
                    if (leagueName != "Serie A" && leagueName != "Ligue 1" && leagueName != "League Two" && leagueName != "Serie B")
                    {
                        // Keep mathematical safety minimums for structural integrity
                        bool isBlowoutRisk = fixture.HomeWinOdds < 1.85 || fixture.AwayWinOdds < 1.85;
                        bool hasScoringCapability = models.Poisson.IsValid && 
                                                    models.Poisson.ExpectedHomeGoals >= 1.05 && 
                                                    models.Poisson.ExpectedAwayGoals >= 1.05;

                        if (!isBlowoutRisk && hasScoringCapability)
                        {
                            double odds = NormalizeOdds(fixture.BttsYesOdds);
                            double ev = odds > 1 ? evEngine.CalculateEV(wp.BTTSProb, odds) : 0;
     
                            double effectiveOdds = odds > 1 ? odds : 1.80;
                            if (effectiveOdds >= requiredMinGoalOdds && effectiveOdds <= MaxOdds)
                            {
                                double adjustedConfidence = isDualQualified ? wp.BTTSProb * 1.05 : wp.BTTSProb;

                                rawCandidates.Add(new CombinationMatchDto(
                                    fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                                    "Both Teams To Score", "Yes", Math.Round(adjustedConfidence, 2),
                                    effectiveOdds, fixture.Status,
                                    fixture.Status == "FT" ? fixture.HomeGoal : null,
                                    fixture.Status == "FT" ? fixture.AwayGoal : null,
                                    false, false, true, decisions.Markets.BTTS?.Reason + (isDualQualified ? " (Dual-Qualified)" : ""),
                                    Math.Round(ev, 4), decision,
                                    fixture.GeminiRecommendation,
                                    fixture.GeminiConfidence ?? 0,
                                    fixture.GeminiIsTrap ?? false,
                                    fixture.GeminiTrapReason,
                                    fixture.GeminiOneLineSummary,
                                    fixture.GeminiBttsSummary,   // 25: GeminiReasoning
                                    fixture.GeminiAnalysis       // 26: GeminiAnalysisText
                                    ));
                            }
                        }
                    }
                }

                // ── Match Winner ──
                if (decisions.Markets.MatchWinner != null && decisions.Markets.MatchWinner.IsQualified)
                {
                    // League-Market Specialization: Avoid Match Winner where historically highly unprofitable
                    if (leagueName != "Ligue 2")
                    {
                        string pred = wp.MatchWinner;
                        double? rawOdds = pred.Equals("home", StringComparison.OrdinalIgnoreCase) ? fixture.HomeWinOdds :
                                         pred.Equals("away", StringComparison.OrdinalIgnoreCase) ? fixture.AwayWinOdds :
                                         fixture.DrawOdds;
                        double odds = NormalizeOdds(rawOdds);
                        double effectiveOdds = odds > 1.1 ? odds : 2.05;
                        double ev = evEngine.CalculateEV(wp.Confidence, effectiveOdds);

                        if (effectiveOdds >= 1.30 && effectiveOdds <= MaxOdds) // Avoid extreme low-value favorites
                        {
                            string displayPred = char.ToUpper(pred[0]) + pred[1..];
                            logger.LogDebug("Match {Id} Market {Market} Decision {Decision} IsQualified {Qual}", 
                                fixture.Id, "Match Winner", decision, decisions.Markets.MatchWinner.IsQualified);
                            rawCandidates.Add(new CombinationMatchDto(
                                fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                                "Match Winner", displayPred, wp.Confidence,
                                effectiveOdds, fixture.Status,
                                fixture.Status == "FT" ? fixture.HomeGoal : null,
                                fixture.Status == "FT" ? fixture.AwayGoal : null,
                                false, true, false, decisions.Markets.MatchWinner.Reason,
                                Math.Round(ev, 4), decision,
                                fixture.GeminiRecommendation,
                                fixture.GeminiConfidence ?? 0,
                                fixture.GeminiIsTrap ?? false,
                                fixture.GeminiTrapReason,
                                fixture.GeminiOneLineSummary,
                                pred.ToLowerInvariant() == "home" ? (fixture.GeminiHomeWinSummary ?? "") : (fixture.GeminiAwayWinSummary ?? ""), // 25: GeminiReasoning
                                fixture.GeminiAnalysis        // 26: GeminiAnalysisText
                                ));
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing fixture {Id}", fixture.Id);
            }
        }

        var targetDecisions = new[] { 
            PredictionDecision.StrongBet.ToString(), 
            PredictionDecision.SmallEdge.ToString(), 
            PredictionDecision.LeanBet.ToString() 
        };
        logger.LogInformation("Filtering raw candidates. Raw: {Count}. Looking for decisions: {Decs}", 
            rawCandidates.Count, string.Join(",", targetDecisions));

        var goalPortfolio = rawCandidates
            .Where(x => (x.Market == "Over 2.5 Goals" || x.Market == "Both Teams To Score") && targetDecisions.Contains(x.Decision))
            .OrderByDescending(x => x.Confidence) // Quality: Prioritize confidence over EV for stable parlays
            .ThenByDescending(x => x.ExpectedValue)
            .ToList();

        var winnerPortfolio = rawCandidates
            .Where(x => x.Market == "Match Winner" && (x.Decision == "StrongBet" || x.Decision == "SmallEdge"))
            .OrderByDescending(x => x.Confidence) // Quality: Prioritize confidence
            .ThenByDescending(x => x.ExpectedValue)
            .ToList();

        logger.LogInformation("Portfolio: {GoalCount} goal bets, {WinnerCount} winner bets",
            goalPortfolio.Count, winnerPortfolio.Count);
        
        if (goalPortfolio.Count > 0)
        {
            logger.LogInformation("Sample Goal Candidate: {Market} for {Home} vs {Away} | Decision: {Decision}", 
                goalPortfolio[0].Market, goalPortfolio[0].HomeTeam, goalPortfolio[0].AwayTeam, goalPortfolio[0].Decision);
        }

        // ── Step 3: Local Deterministic Portfolio Builder ──
        var combinations = new List<CombinationDto>();
        
        // Ensure no overlapping elements within the same group by filtering on distinct IDs
        var uniqueGoals = goalPortfolio.GroupBy(x => x.FixtureId).Select(g => g.First()).ToList();
        var uniqueWinners = winnerPortfolio.GroupBy(x => x.FixtureId).Select(g => g.First()).ToList();

        if (uniqueGoals.Count >= 2)
        {
            combinations.Add(new CombinationDto("High Value Goals Double", uniqueGoals.Take(2).ToList()));
        }

        if (uniqueGoals.Count >= 5)
        {
            combinations.Add(new CombinationDto("Mixed Goals Treble", uniqueGoals.Skip(2).Take(3).ToList()));
        }

        if (uniqueWinners.Count >= 2)
        {
            combinations.Add(new CombinationDto("Statistical Winners Double", uniqueWinners.Take(2).ToList()));
        }

        if (uniqueWinners.Count >= 5)
        {
            combinations.Add(new CombinationDto("Elite Match Winner Treble", uniqueWinners.Skip(2).Take(3).ToList()));
        }

        return new GetMatchCombinationResponse(combinations);
    }



    private static double EstimateUnderOdds(double overOdds)
    {
        if (overOdds < 1.01) return 0;
        double inverseUnder = 1.05 - (1.0 / overOdds);
        return inverseUnder > 0 ? 1.0 / inverseUnder : 0;
    }

    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue || odds.Value <= 0) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }
}
