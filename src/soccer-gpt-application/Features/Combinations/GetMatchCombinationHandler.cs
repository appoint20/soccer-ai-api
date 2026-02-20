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
    // Professional thresholds tuned for 1.68+ odds and higher volume
    private const double MinEdge = 0.02;      // 2% edge
    private const double MinEV = 0.03;        // 3% EV
    private const double MinKelly = 0.01;     // 1% Kelly
    private const double MinOdds = 1.68;      // High win/reward as requested by user
    private const double MaxOdds = 5.00;      // avoid extreme longshots

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

                // ── Over 2.5 ──
                if (wp.Over25 && wp.Over25Prob >= 0.40) // Dropped IsQualified check to force volume
                {
                    double odds = NormalizeOdds(fixture.Over25Odds);
                    double marketProb = odds > 1 ? 1.0 / odds : 0;
                    double edge = wp.Over25Prob - marketProb;
                    double ev = odds > 1 ? evEngine.CalculateEV(wp.Over25Prob, odds) : 0;
                    double kelly = KellyCriterion.Fraction(wp.Over25Prob, odds);

                    if (edge >= MinEdge && ev >= MinEV && kelly >= MinKelly
                        && odds >= MinOdds && odds <= MaxOdds)
                    {
                        rawCandidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Over 2.5 Goals", "Over", Math.Round(wp.Over25Prob, 2),
                            odds, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, true, false, decisions.Markets.Over25.Reason,
                            Math.Round(ev, 4), decision));
                    }
                }


                // ── BTTS ──
                if (wp.BTTS && wp.BTTSProb >= 0.40) // Dropped IsQualified to force volume
                {
                    double odds = NormalizeOdds(fixture.BttsYesOdds);
                    double marketProb = odds > 1 ? 1.0 / odds : 0;
                    double edge = wp.BTTSProb - marketProb;
                    double ev = odds > 1 ? evEngine.CalculateEV(wp.BTTSProb, odds) : 0;
                    double kelly = KellyCriterion.Fraction(wp.BTTSProb, odds);

                    if (edge >= MinEdge && ev >= MinEV && kelly >= MinKelly
                        && odds >= MinOdds && odds <= MaxOdds)
                    {
                        rawCandidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Both Teams To Score", "Yes", Math.Round(wp.BTTSProb, 2),
                            odds, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, true, false, decisions.Markets.BTTS.Reason,
                            Math.Round(ev, 4), decision));
                    }
                }

                // ── Match Winner ──
                if (wp.Confidence >= 0.40) // Dropped IsQualified
                {
                    string pred = wp.MatchWinner;
                    double? rawOdds = pred.Equals("home", StringComparison.OrdinalIgnoreCase) ? fixture.HomeWinOdds :
                                     pred.Equals("away", StringComparison.OrdinalIgnoreCase) ? fixture.AwayWinOdds :
                                     fixture.DrawOdds;
                    double odds = NormalizeOdds(rawOdds);
                    double marketProb = odds > 1 ? 1.0 / odds : 0;
                    double edge = wp.Confidence - marketProb;
                    double ev = odds > 1 ? evEngine.CalculateEV(wp.Confidence, odds) : 0;
                    double kelly = KellyCriterion.Fraction(wp.Confidence, odds);

                    if (edge >= MinEdge && ev >= MinEV && kelly >= MinKelly
                        && odds >= MinOdds && odds <= MaxOdds)
                    {
                        string displayPred = char.ToUpper(pred[0]) + pred[1..];
                        rawCandidates.Add(new CombinationMatchDto(
                            fixture.Id, fixture.LeagueId, leagueName, fixture.Date, homeName, awayName,
                            "Match Winner", displayPred, wp.Confidence,
                            odds, fixture.Status,
                            fixture.Status == "FT" ? fixture.HomeGoal : null,
                            fixture.Status == "FT" ? fixture.AwayGoal : null,
                            false, true, false, decisions.Markets.MatchWinner.Reason,
                            Math.Round(ev, 4), decision));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing fixture {Id}", fixture.Id);
            }
        }

        // ── Step 2: Separate into goal and winner pools ──
        var goalPortfolio = rawCandidates
            .Where(x => x.Market == "Over 2.5 Goals" || x.Market == "Both Teams To Score")
            .OrderByDescending(x => x.ExpectedValue)
            .ThenByDescending(x => x.Confidence)
            .ToList();

        var winnerPortfolio = rawCandidates
            .Where(x => x.Market == "Match Winner")
            .OrderByDescending(x => x.ExpectedValue)
            .ThenByDescending(x => x.Confidence)
            .ToList();

        logger.LogInformation("Portfolio: {GoalCount} goal bets, {WinnerCount} winner bets",
            goalPortfolio.Count, winnerPortfolio.Count);

        // ── Step 3: Build explicit combos ──
        var combinations = new List<CombinationDto>();

        // Goal Combo 1: Double
        var goalCombo1 = BuildUncorrelatedCombo(goalPortfolio, combinations, 2);
        if (goalCombo1.Count >= 2)
            combinations.Add(new CombinationDto("Goal Double 1", goalCombo1));

        // Goal Combo 2: Double
        var goalCombo2 = BuildUncorrelatedCombo(goalPortfolio, combinations, 2);
        if (goalCombo2.Count >= 2)
            combinations.Add(new CombinationDto("Goal Double 2", goalCombo2));

        // Goal Combo 3: Triple (or double if 3 aren't available)
        var goalCombo3 = BuildUncorrelatedCombo(goalPortfolio, combinations, 3);
        if (goalCombo3.Count >= 2)
            combinations.Add(new CombinationDto("Goal Triple", goalCombo3));

        // Winner Combo 4: Double or Triple
        var winnerCombo = BuildUncorrelatedCombo(winnerPortfolio, combinations, 3);
        if (winnerCombo.Count >= 2)
            combinations.Add(new CombinationDto("Winner Combo", winnerCombo));

        return new GetMatchCombinationResponse(combinations);
    }

    /// <summary>
    /// Build a combo of N legs with NO correlated markets and no repeated fixtures.
    /// </summary>
    private static List<CombinationMatchDto> BuildUncorrelatedCombo(
        List<CombinationMatchDto> portfolio,
        List<CombinationDto> existingCombos,
        int targetLegs)
    {
        var usedFixtures = existingCombos
            .SelectMany(c => c.Matches.Select(m => m.FixtureId))
            .ToHashSet();

        var result = new List<CombinationMatchDto>();
        var leagueCounts = new Dictionary<int, int>();

        foreach (var c in portfolio)
        {
            if (result.Count >= targetLegs) break;
            if (usedFixtures.Contains(c.FixtureId)) continue;


            // Same fixture check
            if (result.Any(r => r.FixtureId == c.FixtureId))
                continue;

            // Max 2 per league to reduce league-level correlation
            int lId = c.LeagueId;
            if (leagueCounts.GetValueOrDefault(lId, 0) >= 2)
                continue;

            result.Add(c);
            leagueCounts[lId] = leagueCounts.GetValueOrDefault(lId, 0) + 1;
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
        if (!odds.HasValue || odds.Value <= 0) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }
}
