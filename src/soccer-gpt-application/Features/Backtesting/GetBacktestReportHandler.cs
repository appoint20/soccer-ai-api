using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using System.Collections.Concurrent;

namespace soccer_gpt_application.Features.Backtesting;

public class GetBacktestReportHandler(
    IApplicationDbContext dbContext,
    IServiceProvider serviceProvider,
    ILogger<GetBacktestReportHandler> logger)
    : IRequestHandler<GetBacktestReportQuery, GetBacktestReportResponse>
{
    public async Task<GetBacktestReportResponse> Handle(
        IReceiveContext<GetBacktestReportQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Generating backtest report for last {Weeks} weeks with {Stake} stake.", query.WeeksBack, query.Stake);

        var startDate = DateTime.UtcNow.Date.AddDays(-query.WeeksBack * 7);
        var endDate = DateTime.UtcNow.Date;

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Status == "FT" && f.Date >= startDate && f.Date < endDate)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Found {Count} finished fixtures for backtesting.", fixtures.Count);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name, cancellationToken);

        // 1. Analyze all fixtures in parallel
        var evaluatedLegs = new ConcurrentBag<EvaluatedLeg>();
        var matchDetails = new ConcurrentBag<MatchBacktestDetail>();
        var geminiCorrectBag = new ConcurrentBag<bool>();
        var geminiTotalBag = new ConcurrentBag<bool>();
        var trapCorrectBag = new ConcurrentBag<bool>();
        var trapTotalBag = new ConcurrentBag<bool>();

        await Parallel.ForEachAsync(fixtures, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = cancellationToken }, async (fixture, ct) =>
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, ct);
                if (analysis.Prediction == null) return;

                var decisions = analysis.Decisions;
                var homeName = teams.GetValueOrDefault(fixture.HomeTeamId, "Unknown");
                var awayName = teams.GetValueOrDefault(fixture.AwayTeamId, "Unknown");
                var actualHome = fixture.HomeGoal;
                var actualAway = fixture.AwayGoal;
                int totalGoals = actualHome + actualAway;
                string score = $"{actualHome}-{actualAway}";

                // --- Gemini Stats ---
                if (!string.IsNullOrWhiteSpace(fixture.GeminiRecommendation))
                {
                    bool isGeminiCorrect = false;
                    bool relevantGemini = true;
                    switch (fixture.GeminiRecommendation)
                    {
                        case "Over 2.5 Goals": isGeminiCorrect = totalGoals > 2; break;
                        case "Under 2.5 Goals": isGeminiCorrect = totalGoals < 3; break;
                        case "BTTS": isGeminiCorrect = actualHome > 0 && actualAway > 0; break;
                        case "Match Winner (Home)": isGeminiCorrect = actualHome > actualAway; break;
                        case "Match Winner (Away)": isGeminiCorrect = actualAway > actualHome; break;
                        default: relevantGemini = false; break;
                    }
                    if (relevantGemini)
                    {
                        geminiTotalBag.Add(true);
                        if (isGeminiCorrect) geminiCorrectBag.Add(true);
                    }
                }

                // --- Trap Stats (Was the trap detection correct?) ---
                // Incorporate both System Traps and Gemini Traps
                bool isSystemTrap = decisions.Trap.IsTrap;
                bool isGeminiTrap = fixture.GeminiIsTrap ?? false;

                if (isSystemTrap || isGeminiTrap)
                {
                    trapTotalBag.Add(true);
                    
                    // A trap detection is "correct" if the model's primary prediction failed
                    bool modelCorrect = false;
                    if (analysis.Prediction.Over25Prob > 0.6) modelCorrect = totalGoals > 2;
                    else if (analysis.Prediction.BTTSProb > 0.6) modelCorrect = actualHome > 0 && actualAway > 0;
                    else if (analysis.Prediction.Confidence > 0.6)
                    {
                        string winner = actualHome > actualAway ? "home" : actualHome == actualAway ? "draw" : "away";
                        modelCorrect = analysis.Prediction.MatchWinner.Equals(winner, StringComparison.OrdinalIgnoreCase);
                    }
                    
                    if (!modelCorrect) trapCorrectBag.Add(true);
                }

                // Add Match Detail
                string bestPred = analysis.Prediction.Over25Prob > analysis.Prediction.BTTSProb ? 
                    $"Over 2.5 ({analysis.Prediction.Over25Prob:P0})" : 
                    $"BTTS ({analysis.Prediction.BTTSProb:P0})";
                
                bool isBestCorrect = analysis.Prediction.Over25Prob > analysis.Prediction.BTTSProb ?
                    totalGoals > 2 : (actualHome > 0 && actualAway > 0);

                matchDetails.Add(new MatchBacktestDetail
                {
                    Date = fixture.Date,
                    League = analysis.LeagueName,
                    MatchName = $"{homeName} vs {awayName}",
                    Score = score,
                    Prediction = bestPred,
                    IsCorrect = isBestCorrect,
                    Decision = decisions.Decision.ToString(),
                    IsTrap = isSystemTrap,
                    TrapReason = decisions.Trap.Reason,
                    GeminiRecommendation = fixture.GeminiRecommendation ?? "None",
                    GeminiIsTrap = isGeminiTrap
                });

                // Skip legs for combinations if Avoid/Trap (original logic preserved for ROI)
                if (decisions.Decision == PredictionDecision.Avoid || isSystemTrap || isGeminiTrap) return;

                // Over 2.5
                if (decisions.Markets.Over25?.IsQualified == true)
                {
                    bool isCorrect = totalGoals > 2;
                    double odds = NormalizeOdds(fixture.Over25Odds);
                    if (odds >= 1.50)
                        evaluatedLegs.Add(new EvaluatedLeg(fixture.Date.Date, analysis.LeagueName, "Over 2.5 Goals", analysis.Prediction.Over25Prob, odds, isCorrect, decisions.Decision.ToString()));
                }

                // BTTS
                if (decisions.Markets.BTTS?.IsQualified == true)
                {
                    bool isCorrect = (actualHome > 0 && actualAway > 0);
                    double odds = NormalizeOdds(fixture.BttsYesOdds);
                    if (odds >= 1.50)
                        evaluatedLegs.Add(new EvaluatedLeg(fixture.Date.Date, analysis.LeagueName, "Both Teams To Score", analysis.Prediction.BTTSProb, odds, isCorrect, decisions.Decision.ToString()));
                }

                // Winner
                if (decisions.Markets.MatchWinner?.IsQualified == true)
                {
                    string pred = analysis.Prediction.MatchWinner;
                    string actualWinner = actualHome > actualAway ? "home" : actualHome == actualAway ? "draw" : "away";
                    bool isCorrect = pred.Equals(actualWinner, StringComparison.OrdinalIgnoreCase);
                    
                    double rawOdds = pred.Equals("home", StringComparison.OrdinalIgnoreCase) ? fixture.HomeWinOdds ?? 0 :
                                     pred.Equals("away", StringComparison.OrdinalIgnoreCase) ? fixture.AwayWinOdds ?? 0 :
                                     fixture.DrawOdds ?? 0;
                    double odds = NormalizeOdds(rawOdds);
                    
                    if (odds >= 1.30)
                        evaluatedLegs.Add(new EvaluatedLeg(fixture.Date.Date, analysis.LeagueName, "Match Winner", analysis.Prediction.Confidence, odds, isCorrect, decisions.Decision.ToString()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to analyze fixture {Id} during backtest.", fixture.Id);
            }
        });

        var legs = evaluatedLegs.ToList();
        
        // 2. Aggregate Accuracy
        var markets = legs.GroupBy(l => l.Market).Select(g => new MarketAccuracy
        {
            Market = g.Key,
            Total = g.Count(),
            Correct = g.Count(x => x.IsCorrect),
            Accuracy = Math.Round(g.Average(x => x.IsCorrect ? 1.0 : 0.0) * 100, 1)
        }).ToList();

        var leagues = legs.GroupBy(l => l.League).Select(g => new LeagueAccuracy
        {
            League = g.Key,
            Total = g.Count(),
            Correct = g.Count(x => x.IsCorrect),
            Accuracy = Math.Round(g.Average(x => x.IsCorrect ? 1.0 : 0.0) * 100, 1)
        }).ToList();

        var leagueMarkets = legs.GroupBy(l => new { l.League, l.Market }).Select(g => new LeagueMarketAccuracy
        {
            League = g.Key.League,
            Market = g.Key.Market,
            Total = g.Count(),
            Correct = g.Count(x => x.IsCorrect),
            Accuracy = Math.Round(g.Average(x => x.IsCorrect ? 1.0 : 0.0) * 100, 1)
        }).ToList();

        // 3. Daily Combinations & ROI Sub-simulation
        var dailyCombos = new List<TheoreticalCombo>();
        
        foreach (var dayGroup in legs.GroupBy(l => l.Date))
        {
            var dailyLegs = dayGroup.ToList();
            
            var goalLegs = dailyLegs.Where(x => (x.Market == "Over 2.5 Goals" || x.Market == "Both Teams To Score") && 
                (x.Decision == "StrongBet" || x.Decision == "SmallEdge" || x.Decision == "LeanBet"))
                .OrderByDescending(x => x.Confidence).ToList();

            var winnerLegs = dailyLegs.Where(x => x.Market == "Match Winner" && 
                (x.Decision == "StrongBet" || x.Decision == "SmallEdge"))
                .OrderByDescending(x => x.Confidence).ToList();

            if (goalLegs.Count >= 2) dailyCombos.Add(BuildCombo(goalLegs.Take(2)));
            if (goalLegs.Count >= 5) dailyCombos.Add(BuildCombo(goalLegs.Skip(2).Take(3)));
            if (winnerLegs.Count >= 2) dailyCombos.Add(BuildCombo(winnerLegs.Take(2)));
            if (winnerLegs.Count >= 5) dailyCombos.Add(BuildCombo(winnerLegs.Skip(2).Take(3)));
        }

        double totalStaked = dailyCombos.Count * query.Stake;
        double totalReturned = dailyCombos.Where(c => c.IsWon).Sum(c => c.Odds * query.Stake);
        double profit = totalReturned - totalStaked;
        double roi = totalStaked > 0 ? (profit / totalStaked) * 100 : 0;

        int totalLegsInCombos = dailyCombos.Sum(c => c.LegCount);
        int correctLegsInCombos = dailyCombos.Sum(c => c.CorrectLegCount);
        double baseHitRate = totalLegsInCombos > 0 ? (double)correctLegsInCombos / totalLegsInCombos * 100 : 0;
        double comboWinRate = dailyCombos.Count > 0 ? (double)dailyCombos.Count(c => c.IsWon) / dailyCombos.Count * 100 : 0;

        return new GetBacktestReportResponse
        {
            Summary = new BacktestSummary
            {
                CombosTotal = dailyCombos.Count,
                CombosWon = dailyCombos.Count(c => c.IsWon),
                TotalStakedUnits = dailyCombos.Count, // Base 1 unit
                TotalReturnedUnits = Math.Round(totalReturned / query.Stake, 2),
                PlUnits = Math.Round(profit / query.Stake, 2),
                RoiPercent = Math.Round(roi, 2),
                WinRate = Math.Round(comboWinRate, 1),
                LegHitRate = Math.Round(baseHitRate, 1)
            },
            Markets = markets.OrderByDescending(x => x.Accuracy).ToList(),
            Leagues = leagues.OrderByDescending(x => x.Accuracy).ToList(),
            LeagueMarkets = leagueMarkets.OrderByDescending(x => x.Accuracy).ToList(),
            Matches = matchDetails.OrderByDescending(x => x.Date).ToList(),
            GeminiStats = new AccuracyStats { TotalCount = geminiTotalBag.Count, CorrectCount = geminiCorrectBag.Count },
            TrapStats = new AccuracyStats { TotalCount = trapTotalBag.Count, CorrectCount = trapCorrectBag.Count }
        };
    }

    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue || odds.Value <= 0) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }

    private static TheoreticalCombo BuildCombo(IEnumerable<EvaluatedLeg> comboLegs)
    {
        var list = comboLegs.ToList();
        double totalOdds = 1.0;
        int correct = 0;
        foreach (var leg in list)
        {
            totalOdds *= leg.Odds;
            if (leg.IsCorrect) correct++;
        }
        return new TheoreticalCombo(totalOdds, list.Count, correct, correct == list.Count);
    }
}

public record EvaluatedLeg(DateTime Date, string League, string Market, double Confidence, double Odds, bool IsCorrect, string Decision);
public record TheoreticalCombo(double Odds, int LegCount, int CorrectLegCount, bool IsWon);
