using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using System.Collections.Concurrent;
using System.Globalization;

namespace SoccerAi.Application.Features.Backtesting;

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
        logger.LogInformation("Generating backtest report for last {Weeks} weeks with €{Stake} stake.", query.WeeksBack, query.Stake);

        var startDate = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-query.WeeksBack * 7), TimeSpan.Zero);
        var endDate = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        var allFixtures = await dbContext.Fixtures
            .Where(f => f.Status == "FT")
            .ToListAsync(cancellationToken);

        var fixtures = allFixtures
            .Where(f => f.Date >= startDate && f.Date < endDate)
            .ToList();

        logger.LogInformation("Found {Count} finished fixtures for backtesting.", fixtures.Count);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name, cancellationToken);

        // 1. Analyze all fixtures in parallel
        var evaluatedLegs = new ConcurrentBag<EvaluatedLeg>();

        await Parallel.ForEachAsync(fixtures, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = cancellationToken }, async (fixture, ct) =>
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", ct);
                if (analysis.Prediction == null) return;

                var decisions = analysis.Decisions;
                var actualHome = fixture.HomeGoal;
                var actualAway = fixture.AwayGoal;
                int totalGoals = actualHome + actualAway;

                bool isSystemTrap = decisions.Trap.IsTrap;

                // Skip legs for combinations if Avoid/Trap
                if (decisions.Decision == PredictionDecision.Avoid || isSystemTrap) return;

                // Over 2.5
                if (decisions.Markets.Over25?.IsQualified == true)
                {
                    bool isCorrect = totalGoals > 2;
                    double odds = NormalizeOdds(fixture.Over25Odds);
                    if (odds >= 1.50)
                        evaluatedLegs.Add(new EvaluatedLeg(fixture.Date, analysis.LeagueName, "Over 2.5 Goals", analysis.Prediction.Over25Prob, odds, isCorrect, decisions.Decision.ToString()));
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

        // 2. Daily Combinations & ROI
        var dailyCombos = new List<TheoreticalCombo>();

        foreach (var dayGroup in legs.GroupBy(l => l.Date.Date))
        {
            var dailyLegs = dayGroup.ToList();

            var goalLegs = dailyLegs.Where(x => (x.Market == "Over 2.5 Goals" || x.Market == "Both Teams To Score") &&
                (x.Decision == "StrongBet" || x.Decision == "SmallEdge" || x.Decision == "LeanBet"))
                .OrderByDescending(x => x.Confidence).ToList();

            var winnerLegs = dailyLegs.Where(x => x.Market == "Match Winner" &&
                (x.Decision == "StrongBet" || x.Decision == "SmallEdge"))
                .OrderByDescending(x => x.Confidence).ToList();

            if (goalLegs.Count >= 2) dailyCombos.Add(BuildCombo(goalLegs.Take(2), dayGroup.Key));
            if (goalLegs.Count >= 5) dailyCombos.Add(BuildCombo(goalLegs.Skip(2).Take(3), dayGroup.Key));
            if (winnerLegs.Count >= 2) dailyCombos.Add(BuildCombo(winnerLegs.Take(2), dayGroup.Key));
            if (winnerLegs.Count >= 5) dailyCombos.Add(BuildCombo(winnerLegs.Skip(2).Take(3), dayGroup.Key));
        }

        double totalStaked = dailyCombos.Count * query.Stake;
        double totalReturned = dailyCombos.Where(c => c.IsWon).Sum(c => c.Odds * query.Stake);
        double profit = totalReturned - totalStaked;
        double roi = totalStaked > 0 ? (profit / totalStaked) * 100 : 0;
        double comboWinRate = dailyCombos.Count > 0 ? (double)dailyCombos.Count(c => c.IsWon) / dailyCombos.Count * 100 : 0;

        int totalLegsInCombos = dailyCombos.Sum(c => c.LegCount);
        int correctLegsInCombos = dailyCombos.Sum(c => c.CorrectLegCount);
        double legHitRate = totalLegsInCombos > 0 ? (double)correctLegsInCombos / totalLegsInCombos * 100 : 0;

        // 3. Weekly Breakdown (group combos by ISO week)
        var weeklyBreakdown = dailyCombos
            .GroupBy(c => ISOWeek.GetWeekOfYear(c.Date))
            .OrderBy(g => g.Key)
            .Select((g, index) =>
            {
                int weekBets = g.Count();
                int weekWon = g.Count(c => c.IsWon);
                double weekStaked = weekBets * query.Stake;
                double weekReturned = g.Where(c => c.IsWon).Sum(c => c.Odds * query.Stake);
                double weekRoi = weekStaked > 0 ? ((weekReturned - weekStaked) / weekStaked) * 100 : 0;
                return new WeeklyBreakdown
                {
                    Week = $"W{index + 1}",
                    TotalBets = weekBets,
                    BetsWon = weekWon,
                    RoiPercent = Math.Round(weekRoi, 1)
                };
            }).ToList();

        // 4. League Accuracy (BTTS and Over 2.5 per league)
        var leagueGroups = legs.GroupBy(l => l.League);
        var leagueAccuracy = leagueGroups.Select(g =>
        {
            var bttsLegs = g.Where(l => l.Market == "Both Teams To Score").ToList();
            var over25Legs = g.Where(l => l.Market == "Over 2.5 Goals").ToList();

            double bttsAcc = bttsLegs.Count > 0 ? Math.Round(bttsLegs.Average(l => l.IsCorrect ? 1.0 : 0.0) * 100, 1) : 0;
            double over25Acc = over25Legs.Count > 0 ? Math.Round(over25Legs.Average(l => l.IsCorrect ? 1.0 : 0.0) * 100, 1) : 0;

            return new LeagueAccuracy
            {
                League = g.Key,
                BttsAccuracy = bttsAcc,
                Over25Accuracy = over25Acc
            };
        })
        .Where(la => la.BttsAccuracy > 0 || la.Over25Accuracy > 0)
        .OrderByDescending(la => (la.BttsAccuracy + la.Over25Accuracy) / 2)
        .ToList();

        return new GetBacktestReportResponse
        {
            Summary = new BacktestSummary
            {
                TotalRoi = Math.Round(roi, 1),
                TotalStaked = Math.Round(totalStaked, 2),
                TotalReturned = Math.Round(totalReturned, 2),
                CombinationAccuracy = Math.Round(comboWinRate, 1),
                WinRate = Math.Round(comboWinRate, 1),
                CombosTotal = dailyCombos.Count,
                CombosWon = dailyCombos.Count(c => c.IsWon),
                MatchAnalysisAccuracy = Math.Round(legHitRate, 1),
                TotalLegs = totalLegsInCombos,
                CorrectLegs = correctLegsInCombos
            },
            WeeklyBreakdown = weeklyBreakdown,
            LeagueAccuracy = leagueAccuracy
        };
    }

    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue || odds.Value <= 0) return 0;
        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }

    private static TheoreticalCombo BuildCombo(IEnumerable<EvaluatedLeg> comboLegs, DateTime date)
    {
        var list = comboLegs.ToList();
        double totalOdds = 1.0;
        int correct = 0;
        foreach (var leg in list)
        {
            totalOdds *= leg.Odds;
            if (leg.IsCorrect) correct++;
        }
        return new TheoreticalCombo(date, totalOdds, list.Count, correct, correct == list.Count);
    }
}

public record EvaluatedLeg(DateTimeOffset Date, string League, string Market, double Confidence, double Odds, bool IsCorrect, string Decision);
public record TheoreticalCombo(DateTime Date, double Odds, int LegCount, int CorrectLegCount, bool IsWon);
