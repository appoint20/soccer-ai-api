using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Features.Combinations;

/// <summary>
/// Combination handler — uses the shared IMatchAnalysisService for ALL analysis,
/// then selects best qualified predictions and assembles 3-leg parlays.
/// No duplicate logic — same pipeline as the analysis endpoint.
/// </summary>
public class GetMatchCombinationHandler(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
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

        logger.LogInformation("Found {Count} fixtures for potential combination", fixtures.Count);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name, cancellationToken);

        var candidates = new List<CombinationMatchDto>();

        foreach (var fixture in fixtures)
        {
            try
            {
                // ── Use shared analysis pipeline (same as /api/analysis) ──
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
                if (analysis.Prediction == null) continue;

                var wp = analysis.Prediction;
                var decisions = analysis.Decisions;
                var models = analysis.Models;
                var homeName = teams.GetValueOrDefault(fixture.HomeTeamId, $"Team {fixture.HomeTeamId}");
                var awayName = teams.GetValueOrDefault(fixture.AwayTeamId, $"Team {fixture.AwayTeamId}");
                var leagueName = analysis.LeagueName;

                // ── Over 2.5 ──
                if (wp.Over25 && decisions.Markets.Over25.IsQualified)
                {
                    double odds = analysis.OddsOver25;
                    // Tier 1 Consensus: ML > 60% AND Total Poisson xG > 2.80
                    double totalXg = models.Poisson.ExpectedHomeGoals + models.Poisson.ExpectedAwayGoals;
                    bool isConsensus = wp.Over25Prob > 0.60 && totalXg > 2.80;
                    
                    // Tier 2 Fallback: Poisson > 55% AND ML > 55% (Reverted to standard fallback)
                    bool isFallback = !isConsensus && models.Poisson.Over25 > 0.55 && wp.Over25Prob > 0.55;

                    if (odds > 1.35)
                    {
                        candidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Over 2.5 Goals", "Over", Math.Round(wp.Over25Prob, 2),
                            fixture.Over25Odds ?? 0, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, isConsensus, isFallback, decisions.Markets.Over25.Reason));
                    }
                }

                // ── Under 2.5 ──
                if (!wp.Over25 && decisions.Markets.LowScoring.IsQualified)
                {
                    double underOdds = EstimateUnderOdds(analysis.OddsOver25);
                    // Tier 1 Consensus: ML < 40% AND Total xG < 2.20
                    double totalXg = models.Poisson.ExpectedHomeGoals + models.Poisson.ExpectedAwayGoals;
                    bool isConsensus = wp.Over25Prob < 0.40 && totalXg < 2.20;
                    
                    // Tier 2 Fallback: Poisson < 45% AND ML < 45%
                    bool isFallback = !isConsensus && models.Poisson.Over25 < 0.45 && wp.Over25Prob < 0.45;

                    if (underOdds > 1.35)
                    {
                        candidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Under 2.5 Goals", "Under", Math.Round(1 - wp.Over25Prob, 2),
                            underOdds, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, isConsensus, isFallback, decisions.Markets.LowScoring.Reason));
                    }
                }

                // ── BTTS ──
                if (wp.BTTS && decisions.Markets.BTTS.IsQualified)
                {
                    double odds = analysis.OddsBttsYes;
                    // Tier 1 Consensus: ML > 60% AND xG > 1.15 each
                    bool isConsensus = wp.BTTSProb > 0.60 && 
                                      models.Poisson.ExpectedHomeGoals > 1.15 && 
                                      models.Poisson.ExpectedAwayGoals > 1.15;
                    
                    // Tier 2 Fallback: Poisson > 55% AND ML > 55% (Reverted)
                    bool isFallback = !isConsensus && models.Poisson.BTTS > 0.55 && wp.BTTSProb > 0.55;

                    if (odds > 1.35)
                    {
                        candidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Both Teams To Score", "Yes", Math.Round(wp.BTTSProb, 2),
                            fixture.BttsYesOdds ?? 0, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, isConsensus, isFallback, decisions.Markets.BTTS.Reason));
                    }
                }

                // ── 2-3 Goals ──
                if (wp.TwoToThreeGoals && decisions.Markets.TwoToThreeGoals.IsQualified)
                {
                    // Tier 1 Consensus: Total xG 2.2-2.8
                    double totalXg = models.Poisson.ExpectedHomeGoals + models.Poisson.ExpectedAwayGoals;
                    bool isConsensus = totalXg >= 2.20 && totalXg <= 2.80;
                    
                    // Tier 2 Fallback: Poisson 2-3 > 40% AND ML > 50% (Reverted)
                    bool isFallback = !isConsensus && models.Poisson.TwoToThreeGoals > 0.40 && wp.TwoToThreeGoalsProb > 0.50;
                    
                    candidates.Add(new CombinationMatchDto(
                        fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                        "2-3 Goals", "Yes", Math.Round(wp.TwoToThreeGoalsProb, 2),
                        0, fixture.Status,
                        fixture.Status == "FT" ? fixture.HomeGoal : null,
                        fixture.Status == "FT" ? fixture.AwayGoal : null,
                        false, isConsensus, isFallback, decisions.Markets.TwoToThreeGoals.Reason));
                }

                // ── Match Winner ──
                if (decisions.Markets.MatchWinner.IsQualified)
                {
                    string pred = wp.MatchWinner;
                    double? odds = pred.Equals("home", StringComparison.OrdinalIgnoreCase) ? fixture.HomeWinOdds :
                                   pred.Equals("away", StringComparison.OrdinalIgnoreCase) ? fixture.AwayWinOdds :
                                   fixture.DrawOdds;
                    double normalizedOdds = NormalizeOdds(odds);

                    // Tier 1 Consensus: Poisson > 45% AND MC > 45% (Proven logic)
                    bool isConsensus = false;
                    bool isFallback = false;
                    
                    if (pred == "home") 
                    {
                        isConsensus = models.Poisson.HomeWin > 0.45 && models.MonteCarlo.HomeWin > 0.45;
                        // Fallback: Just Poisson > 45% (Reverted)
                        isFallback = !isConsensus && models.Poisson.HomeWin > 0.45;
                    }
                    else if (pred == "away") 
                    {
                        isConsensus = models.Poisson.AwayWin > 0.45 && models.MonteCarlo.AwayWin > 0.45;
                        isFallback = !isConsensus && models.Poisson.AwayWin > 0.45;
                    }

                    string displayPred = char.ToUpper(pred[0]) + pred[1..];

                    if (normalizedOdds > 1.20)
                    {
                        candidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Match Winner", displayPred, wp.Confidence,
                            odds ?? 0, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, isConsensus, isFallback, decisions.Markets.MatchWinner.Reason));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing fixture {Id} for combo", fixture.Id);
            }
        }

        // ── Assemble Combinations ─────────────────────────────────
        // Strategy: enforce market diversity within each combo.
        // Each combo should mix markets for uncorrelated risk.

        var combinations = new List<CombinationDto>();
        var usedFixtureIds = new HashSet<int>();

        // Sort: Priority 1 = Consensus, Priority 2 = Fallback (Stats+ML), Priority 3 = Confidence
        var allCandidates = candidates
            .OrderByDescending(x => x.IsConsensus)
            .ThenByDescending(x => x.IsFallback)
            .ThenByDescending(x => x.Confidence)
            .ToList();

        // Strict Candidates for Combo 1 & 2: Consensus OR High Confidence (> 0.58)
        var strictCandidates = allCandidates
            .Where(x => x.IsConsensus || x.Confidence >= 0.59)
            .ToList();

        // Consistently use allCandidates (sorted by Consensus > Fallback > Confidence)
        // This ensures the best matches are available to all strategies.
        for (int i = 1; i <= 5; i++)
        {
            List<CombinationMatchDto> comboMatches;
            if (i <= 3)
            {
                // Combos 1-3: Goal Focused (Over 2.5 / BTTS)
                 comboMatches = BuildGoalCombo(allCandidates, usedFixtureIds);
            }
            else
            {
                // Combos 4-5: Mixed/Diverse Markets
                comboMatches = BuildDiverseCombo(allCandidates, usedFixtureIds);
            }

            // Allow 2-leg combos for Goal Combos, but require 3 for Mix
            int minLegs = i <= 3 ? 2 : 3;
            if (comboMatches.Count < minLegs) continue;

            string comboName = i <= 3 ? $"Goal Combo {i}" : $"Win/Mix Combo {i}";
            combinations.Add(new CombinationDto(comboName, comboMatches));
            foreach (var m in comboMatches) usedFixtureIds.Add(m.FixtureId);
        }

        return new GetMatchCombinationResponse(combinations);
    }

    /// <summary>
    /// Builds a combo strictly from Over 2.5 and BTTS markets.
    /// Improved Logic:
    /// - Tries to find 3 High Quality matches.
    /// - If only 2 are found, returns 2 (Double) instead of forcing a weak 3rd leg.
    /// </summary>
    private static List<CombinationMatchDto> BuildGoalCombo(
        List<CombinationMatchDto> candidates, HashSet<int> usedFixtureIds)
    {
        // Filter: Only Over 2.5 and BTTS
        // STRICT RULE: Only accept matches that meet the High Quality criteria.
        var validCandidates = candidates
            .Where(c => c.Market == "Over 2.5 Goals" || c.Market == "Both Teams To Score")
            // Strict Filter: Must be Consensus OR very high confidence (>0.58)
            .Where(c => c.IsConsensus || c.Confidence >= 0.59) 
            .ToList();

        var result = new List<CombinationMatchDto>();
        var leagueCounts = new Dictionary<int, int>(); 
        var leagueMarkets = new HashSet<(int, string)>(); 
        var currentComboFixtures = new HashSet<int>();
        
        foreach (var c in validCandidates)
        {
            if (result.Count >= 3) break;
            if (usedFixtureIds.Contains(c.FixtureId)) continue;
            if (currentComboFixtures.Contains(c.FixtureId)) continue;
            
            // League constraints
            int lId = c.LeagueId;
            int count = leagueCounts.GetValueOrDefault(lId, 0);
            
            if (count >= 2) continue; 
            if (leagueMarkets.Contains((lId, c.Market))) continue;
            
            result.Add(c);
            leagueCounts[lId] = count + 1;
            leagueMarkets.Add((lId, c.Market));
            currentComboFixtures.Add(c.FixtureId);
        }
        
        // Return result if we have at least 2 strong legs
        return result.Count >= 2 ? result : new List<CombinationMatchDto>(); 
    }

    /// <summary>
    /// Build a 3-leg combo with market diversity: max 1 leg per market type,
    /// each from a different fixture. Prioritizes higher-accuracy markets.
    /// </summary>
    private static List<CombinationMatchDto> BuildDiverseCombo(
        List<CombinationMatchDto> candidates, HashSet<int> usedFixtureIds)
    {
        var result = new List<CombinationMatchDto>();
        var usedMarkets = new HashSet<string>();
        var usedInCombo = new HashSet<int>();

        // Pass 1: pick one leg per market (diverse), highest confidence first
        foreach (var c in candidates)
        {
            if (result.Count >= 3) break;
            if (usedFixtureIds.Contains(c.FixtureId)) continue;
            if (usedInCombo.Contains(c.FixtureId)) continue;
            if (usedMarkets.Contains(c.Market)) continue;

            result.Add(c);
            usedMarkets.Add(c.Market);
            usedInCombo.Add(c.FixtureId);
        }

        // Pass 2: if we still need legs, allow market repeats (different fixtures)
        if (result.Count < 3)
        {
            foreach (var c in candidates)
            {
                if (result.Count >= 3) break;
                if (usedFixtureIds.Contains(c.FixtureId)) continue;
                if (usedInCombo.Contains(c.FixtureId)) continue;

                result.Add(c);
                usedInCombo.Add(c.FixtureId);
            }
        }

        return result;
    }

    private static double EstimateUnderOdds(double overOdds)
    {
        if (overOdds < 1.01) return 0;
        double inverseUnder = 1.05 - (1.0 / overOdds);
        return inverseUnder > 0 ? 1.0 / inverseUnder : 0;
    }

    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }
}
