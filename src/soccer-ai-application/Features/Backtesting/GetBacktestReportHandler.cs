using System.Text.Json;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using System.Collections.Concurrent;
using System.Globalization;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Features.Backtesting;

public class GetBacktestReportHandler(
    IApplicationDbContext dbContext,
    IServiceProvider serviceProvider,
    IChatCombinationEngine engine,
    ILogger<GetBacktestReportHandler> logger)
    : IRequestHandler<GetBacktestReportQuery, GetBacktestReportResponse>
{
    public async Task<GetBacktestReportResponse> Handle(
        IReceiveContext<GetBacktestReportQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        
        if (!query.Refresh)
        {
            var cached = await dbContext.BacktestReports
                .Where(r => r.WeeksBack == query.WeeksBack && r.Stake == query.Stake)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (cached != null)
            {
                var response = JsonSerializer.Deserialize<GetBacktestReportResponse>(cached.ReportJson);
                if (response != null) return response;
            }
        }

        logger.LogInformation("[Backtest] Simulating last {Weeks} weeks. Pure Math Engine.", query.WeeksBack);

        var startDate = DateTimeOffset.UtcNow.AddDays(-query.WeeksBack * 7);
        var endDate = DateTimeOffset.UtcNow;

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Status == "FT" && f.Date >= startDate && f.Date <= endDate)
            .OrderBy(f => f.Date)
            .ToListAsync(cancellationToken);

        // Fetch team metadata for mapping
        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var simulationResults = new List<SimulationCombo>();
        var leagueResults = new List<LeaguePredictionResult>();
        var dayGroups = fixtures.GroupBy(f => f.Date.Date).ToList();

        using var scope = serviceProvider.CreateScope();
        var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();

        foreach (var day in dayGroups)
        {
            var matchAnalyses = new List<MatchAnalysis>();
            foreach (var f in day)
            {
                try
                {
                    var analysisResult = await analysisService.AnalyzeFixtureAsync(f, "en", cancellationToken);
                    if (analysisResult.Prediction != null)
                    {
                        var home = teams.GetValueOrDefault(f.HomeTeamId) ?? new Team { Name = "Home" };
                        var away = teams.GetValueOrDefault(f.AwayTeamId) ?? new Team { Name = "Away" };
                        
                        var mapped = SoccerAi.Application.Services.Analysis.AnalysisResponseMapper.MapToResponse(
                            f, analysisResult, home, away, analysisResult.Gemini);
                        
                        // Track league accuracy for all analyzed matches
                        var pred = analysisResult.Prediction;
                        bool bttsActual = f.HomeGoal > 0 && f.AwayGoal > 0;
                        bool over25Actual = (f.HomeGoal + f.AwayGoal) > 2;

                        leagueResults.Add(new LeaguePredictionResult
                        {
                            League = analysisResult.LeagueName,
                            BttsHit = pred.BTTS == bttsActual,
                            Over25Hit = pred.Over25 == over25Actual
                        });

                        matchAnalyses.Add(mapped);
                    }
                }
                catch { /* Skip failed analysis */ }
            }

            if (matchAnalyses.Count < 5) continue;

            // Simulate the Portfolio Generation
            var portfolios = engine.GenerateCombinations(matchAnalyses, new ChatCombinationIntent 
            { 
                MinSelectionOdds = 1.60,
                SourceType = "SYSTEM"
            });

            foreach (var combo in portfolios)
            {
                bool isFullWin = true;
                foreach (var leg in combo.Matches)
                {
                    var fix = fixtures.First(fx => fx.Id == leg.FixtureId);
                    if (!IsLegWon(leg.Selection, fix))
                    {
                        isFullWin = false;
                        break;
                    }
                }

                simulationResults.Add(new SimulationCombo 
                { 
                    Date = day.Key, 
                    Odds = combo.TotalOdds, 
                    IsWon = isFullWin, 
                    Stake = query.Stake,
                    Return = isFullWin ? combo.TotalOdds * query.Stake : 0,
                    AverageConfidence = combo.Matches.Any() ? combo.Matches.Average(m => m.Confidence) : 0
                });
            }
        }

        return CalculateFinalReport(simulationResults, leagueResults, query.WeeksBack, query.Stake);
    }

    private bool IsLegWon(string selection, Fixture f)
    {
        var goals = f.HomeGoal + f.AwayGoal;
        return selection switch
        {
            "Match Winner (Home)" => f.HomeGoal > f.AwayGoal,
            "Match Winner (Away)" => f.AwayGoal > f.HomeGoal,
            "Draw" => f.HomeGoal == f.AwayGoal,
            "BTTS" => f.HomeGoal > 0 && f.AwayGoal > 0,
            "Over 2.5 Goals" => goals > 2,
            "2-3 Goals" => goals == 2 || goals == 3,
            _ => false
        };
    }

    private GetBacktestReportResponse CalculateFinalReport(List<SimulationCombo> results, List<LeaguePredictionResult> leagueResults, int weeks, double stake)
    {
        // Group by week and apply the 9-combination limit per week
        var weeklyGroups = results.GroupBy(r => ISOWeek.GetWeekOfYear(r.Date))
            .Select(g => 
            {
                // Take the top 9 combinations of the week based on confidence
                var limited = g.OrderByDescending(x => x.AverageConfidence).Take(9).ToList();
                return new 
                {
                    WeekKey = g.Key,
                    Items = limited
                };
            }).ToList();

        var finalSimulations = weeklyGroups.SelectMany(g => g.Items).ToList();

        var totalStaked = finalSimulations.Sum(r => r.Stake);
        var totalReturned = finalSimulations.Sum(r => r.Return);
        var profit = totalReturned - totalStaked;
        var roi = totalStaked > 0 ? (profit / totalStaked) * 100 : 0;

        var weeklyBreakdown = weeklyGroups
            .Select(g => new WeeklyBreakdown
            {
                Week = $"Week {g.WeekKey}",
                TotalCombinations = g.Items.Count,
                CombinationsWon = g.Items.Count(x => x.IsWon),
                StakeAmount = Math.Round(g.Items.Sum(x => x.Stake), 2),
                ProfitLoss = Math.Round(g.Items.Sum(x => x.Return - x.Stake), 2),
                RoiPercent = Math.Round(g.Items.Sum(x => x.Stake) > 0 ? (g.Items.Sum(x => x.Return - x.Stake) / g.Items.Sum(x => x.Stake)) * 100 : 0, 1)
            }).ToList();

        // Calculate League Accuracy
        var leagueAccuracy = leagueResults.GroupBy(l => l.League)
            .Select(g => new LeagueAccuracy
            {
                League = g.Key,
                BttsAccuracy = Math.Round((double)g.Count(x => x.BttsHit) / g.Count() * 100, 1),
                Over25Accuracy = Math.Round((double)g.Count(x => x.Over25Hit) / g.Count() * 100, 1)
            })
            .OrderByDescending(l => l.Over25Accuracy)
            .ToList();

        return new GetBacktestReportResponse
        {
            Summary = new BacktestSummary
            {
                TotalRoi = Math.Round(roi, 1),
                TotalStaked = Math.Round(totalStaked, 2),
                TotalReturned = Math.Round(totalReturned, 2),
                WinRate = Math.Round(finalSimulations.Count > 0 ? (double)finalSimulations.Count(r => r.IsWon) / finalSimulations.Count * 100 : 0, 1),
                CombosTotal = finalSimulations.Count,
                CombosWon = finalSimulations.Count(r => r.IsWon)
            },
            WeeklyBreakdown = weeklyBreakdown,
            LeagueAccuracy = leagueAccuracy
        };
    }

    private class SimulationCombo
    {
        public DateTime Date { get; set; }
        public double Odds { get; set; }
        public bool IsWon { get; set; }
        public double Stake { get; set; }
        public double Return { get; set; }
        public double AverageConfidence { get; set; }
    }

    private class LeaguePredictionResult
    {
        public string League { get; set; } = "";
        public bool BttsHit { get; set; }
        public bool Over25Hit { get; set; }
    }
}
