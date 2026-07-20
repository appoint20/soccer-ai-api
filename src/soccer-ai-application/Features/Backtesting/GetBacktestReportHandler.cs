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
using SoccerAi.Application.Services.Combinations;
using SoccerAi.Application.Services.Evaluation;

namespace SoccerAi.Application.Features.Backtesting;

public class GetBacktestReportHandler(
    IApplicationDbContext dbContext,
    IServiceProvider serviceProvider,
    ILeagueTierService leagueTiers,
    Microsoft.Extensions.Options.IOptions<SoccerAi.Application.Options.ConfluenceOptions> confluenceOptions,
    ILogger<GetBacktestReportHandler> logger)
    : IRequestHandler<GetBacktestReportQuery, GetBacktestReportResponse>
{
    /// <summary>Minimum odds per accumulator leg (unchanged from previous behavior).</summary>
    private const double MinSelectionOdds = 1.50;

    private const int LowSampleThreshold = 30;

    /// <summary>OddsValid: the market's stored odds pass the sanity guard (1.01-15.0).</summary>
    private sealed record MarketSampleRow(
        string Market, string League, double Probability, bool Outcome, bool OddsValid);
    private sealed record HdaSampleRow(double[] Probabilities, int ActualIndex);
    private sealed record QualifiedPickRow(
        string Market, string League, bool Won, double? Odds, IReadOnlyList<string> FiredRules,
        double? Ev, double? KellyStake);
    private sealed record GateOutcomeRow(string Market, string Outcome);
    private sealed record ShadowPickRow(
        string Cohort, string Market, string League, bool Won, double Odds, double? Ev);

    public async Task<GetBacktestReportResponse> Handle(
        IReceiveContext<GetBacktestReportQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        
        // 1. Check persistence layer for cached report
        if (!query.Refresh)
        {
            var cached = await dbContext.BacktestReports
                .Where(r => r.WeeksBack == query.WeeksBack && r.Stake == query.Stake)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (cached != null && cached.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7))
            {
                logger.LogInformation("[Backtest] Cache HIT. Returning persisted report from {Date}", cached.CreatedAt);
                try
                {
                    return JsonSerializer.Deserialize<GetBacktestReportResponse>(cached.ReportJson)!;
                }
                catch
                {
                    logger.LogWarning("[Backtest] Failed to deserialize cached report. Recalculating...");
                }
            }
        }

        logger.LogInformation("[Backtest] Simulating last {Weeks} weeks. Pure Math Engine.", query.WeeksBack);

        var startDate = DateTimeOffset.UtcNow.AddDays(-query.WeeksBack * 7);
        var endDate = DateTimeOffset.UtcNow;

        // Scope: Tier1 focus leagues by default; Tier2 only when enabled by flag.
        var scopedLeagueIds = leagueTiers.GetSyncLeagueIds().ToList();
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Status == "FT" && f.Date >= startDate && f.Date <= endDate
                        && scopedLeagueIds.Contains(f.LeagueId))
            .OrderBy(f => f.Date)
            .ToListAsync(cancellationToken);

        // Fetch team metadata for mapping
        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var simulationResults = new ConcurrentBag<SimulationCombo>();
        var leagueResults = new ConcurrentBag<LeaguePredictionResult>();
        var marketSamples = new ConcurrentBag<MarketSampleRow>();
        var hdaSamples = new ConcurrentBag<HdaSampleRow>();
        var qualifiedPicks = new ConcurrentBag<QualifiedPickRow>();
        var gateOutcomes = new ConcurrentBag<GateOutcomeRow>();
        var shadowPicks = new ConcurrentBag<ShadowPickRow>();
        var dayGroups = fixtures.GroupBy(f => f.Date.Date).ToList();

        // Use a semaphore to limit concurrency and avoid hammering the database/AI
        using var semaphore = new SemaphoreSlim(10); 

        var tasks = dayGroups.Select(async day =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
                var dayPicks = new List<BacktestPick>();

                foreach (var f in day)
                {
                    try
                    {
                        var analysisResult = await analysisService.AnalyzeFixtureAsync(f, "en", false, cancellationToken);
                        if (analysisResult.Prediction == null) continue;

                        var home = teams.GetValueOrDefault(f.HomeTeamId) ?? new Team { Name = "Home" };
                        var away = teams.GetValueOrDefault(f.AwayTeamId) ?? new Team { Name = "Away" };
                        var league = analysisResult.LeagueName;

                        var pred = analysisResult.Prediction;
                        var totalGoals = f.HomeGoal + f.AwayGoal;
                        var bttsActual = f is { HomeGoal: > 0, AwayGoal: > 0 };
                        var over25Actual = totalGoals > 2;
                        var goals23Actual = totalGoals is 2 or 3;
                        var hdaActual = f.HomeGoal > f.AwayGoal ? 0 : f.HomeGoal == f.AwayGoal ? 1 : 2;
                        // Winner pick = stronger non-draw side (draw is its own market)
                        var pickIsHome = pred.HomeProb >= pred.AwayProb;
                        var winnerProb = Math.Max(pred.HomeProb, pred.AwayProb);
                        var pickWon = pickIsHome ? hdaActual == 0 : hdaActual == 2;
                        var drawWon = hdaActual == 1;

                        // ── Probabilistic quality samples (ALL analyzed fixtures) ──
                        var pickOddsRaw = pickIsHome ? f.HomeWinOdds : f.AwayWinOdds;
                        marketSamples.Add(new MarketSampleRow("btts", league, pred.BTTSProb, bttsActual,
                            Services.OddsGuard.IsValid(f.BttsYesOdds)));
                        marketSamples.Add(new MarketSampleRow("over25", league, pred.Over25Prob, over25Actual,
                            Services.OddsGuard.IsValid(f.Over25Odds)));
                        marketSamples.Add(new MarketSampleRow("goals_2_3", league, pred.TwoToThreeGoalsProb, goals23Actual,
                            false)); // no 2-3 Goals odds market is stored
                        marketSamples.Add(new MarketSampleRow("match_winner", league, winnerProb, pickWon,
                            Services.OddsGuard.IsValid(pickOddsRaw)));
                        marketSamples.Add(new MarketSampleRow("draw", league, pred.DrawProb, drawWon,
                            Services.OddsGuard.IsValid(f.DrawOdds)));
                        hdaSamples.Add(new HdaSampleRow(
                            [pred.HomeProb, pred.DrawProb, pred.AwayProb], hdaActual));

                        leagueResults.Add(new LeaguePredictionResult
                        {
                            League = league,
                            BttsHit = pred.BTTS == bttsActual,
                            Over25Hit = pred.Over25 == over25Actual
                        });

                        // ── Qualified picks (headline metric + combo pool) ──
                        var m = analysisResult.Decisions.Markets;
                        var auditByMarket = analysisResult.Decisions.Audit?.Markets
                            .ToDictionary(a => a.Market) ?? [];

                        // Qualification funnel: record every market's gate outcome.
                        foreach (var marketAudit in auditByMarket.Values)
                            gateOutcomes.Add(new GateOutcomeRow(marketAudit.Market, marketAudit.GateOutcome));

                        // ── Shadow cohorts: what the price gates rejected ──
                        bool MarketWon(string market) => market switch
                        {
                            "btts" => bttsActual,
                            "over25" => over25Actual,
                            "goals_2_3" => goals23Actual,
                            "match_winner" => pickWon,
                            "under25" => totalGoals < 3,
                            "draw" => drawWon,
                            _ => false
                        };

                        var minConfirms = analysisResult.Decisions.Audit?.MinConfirmationsRequired
                            ?? confluenceOptions.Value.MinConfirmations;
                        foreach (var marketAudit in auditByMarket.Values)
                        {
                            foreach (var cohort in Services.Decisions.ShadowCohorts.Classify(marketAudit, minConfirms))
                            {
                                shadowPicks.Add(new ShadowPickRow(
                                    cohort, marketAudit.Market, league,
                                    MarketWon(marketAudit.Market),
                                    marketAudit.Odds!.Value, marketAudit.Ev));
                            }
                        }

                        // Named hypothesis: favorites p≥62% at odds 1.40-2.10
                        var pickOddsSafe = Services.OddsGuard.Sanitize(pickOddsRaw);
                        if (Services.Decisions.ShadowCohorts.InWinnerBand(
                                winnerProb, pickOddsSafe, confluenceOptions.Value))
                        {
                            shadowPicks.Add(new ShadowPickRow(
                                Services.Decisions.ShadowCohorts.WinnerBand, "match_winner", league,
                                pickWon, pickOddsSafe!.Value,
                                Math.Round(winnerProb * pickOddsSafe.Value - 1, 4)));
                        }

                        void AddPick(string market, string selection, bool won, double? odds, double prob,
                            string? auditMarket = null)
                        {
                            // Sanity guard: invalid odds (e.g. 185 instead of 1.85) are
                            // EXCLUDED from ROI and combos — never clamped or substituted.
                            var safeOdds = Services.OddsGuard.Sanitize(odds);

                            var audit = auditByMarket.GetValueOrDefault(auditMarket ?? market);
                            var fired = audit?.FiredConfirmRuleIds.ToList() ?? [];
                            qualifiedPicks.Add(new QualifiedPickRow(
                                market, league, won, safeOdds, fired, audit?.Ev, audit?.KellyStake));
                            if (safeOdds is not null)
                                dayPicks.Add(new BacktestPick(
                                    f.Id, league, home.Name, away.Name, selection, prob, safeOdds.Value));
                        }

                        if (m.Over25.IsQualified)
                            AddPick("over25", "Over 2.5 Goals", over25Actual, f.Over25Odds, pred.Over25Prob);
                        if (m.BTTS.IsQualified)
                            AddPick("btts", "BTTS", bttsActual, f.BttsYesOdds, pred.BTTSProb);
                        if (m.TwoToThreeGoals.IsQualified)
                            // No 2-3 Goals odds are stored — pick is tracked for hit
                            // rate but excluded from ROI (no substituted price).
                            AddPick("goals_2_3", "2-3 Goals", goals23Actual, null, pred.TwoToThreeGoalsProb);
                        if (m.MatchWinner.IsQualified)
                            AddPick("match_winner",
                                pickIsHome ? "Match Winner (Home)" : "Match Winner (Away)",
                                pickWon,
                                pickIsHome ? f.HomeWinOdds : f.AwayWinOdds,
                                winnerProb);
                        if (m.Draw.IsQualified)
                            AddPick("draw", "Draw", drawWon, f.DrawOdds, pred.DrawProb, auditMarket: "draw");
                        if (m.LowScoring.IsQualified)
                            AddPick("low_scoring", "Under 2.5 Goals", totalGoals < 3, f.Under25Odds,
                                m.LowScoring.Confidence, auditMarket: "under25");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error analyzing fixture {FixtureId}", f.Id);
                    }
                }

                // Deterministic combination simulation from qualified picks only.
                // (Previously IChatCombinationEngine → LLM call → empty without an
                // API key, which is why combos_total was 0.)
                {
                    var portfolios = BacktestCombinationBuilder.Build(dayPicks, MinSelectionOdds);

                    foreach (var combo in portfolios)
                    {
                        var legResults = new List<LegResult>();
                        bool isFullWin = true;
                        foreach (var leg in combo.Matches)
                        {
                            var fix = fixtures.FirstOrDefault(fx => fx.Id == leg.FixtureId);
                            if (fix == null)
                            {
                                logger.LogWarning("[Backtest] AI returned unknown fixture ID {Id}. Skipping leg.", leg.FixtureId);
                                isFullWin = false;
                                continue;
                            }

                            bool legWon = IsLegWon(leg.Selection, fix);
                            legResults.Add(new LegResult { IsWon = legWon });
                            
                            if (!legWon)
                            {
                                isFullWin = false;
                            }
                        }

                        double avgConfidence = combo.Matches.Any() ? combo.Matches.Average(m => m.Confidence) : 0;
                        
                        simulationResults.Add(new SimulationCombo 
                        { 
                            Date = day.Key, 
                            Odds = combo.TotalOdds, 
                            IsWon = isFullWin, 
                            Stake = query.Stake,
                            Return = isFullWin ? combo.TotalOdds * query.Stake : 0,
                            AverageConfidence = avgConfidence,
                            Legs = legResults
                        });
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var response = CalculateFinalReport(
            simulationResults.ToList(), leagueResults.ToList(),
            marketSamples.ToList(), hdaSamples.ToList(), qualifiedPicks.ToList(),
            gateOutcomes.ToList(), shadowPicks.ToList(), startDate, query.WeeksBack, query.Stake);

        // 2. Persist the report to cache
        try
        {
            var reportEntity = new BacktestReport
            {
                WeeksBack = query.WeeksBack,
                Stake = query.Stake,
                ReportJson = JsonSerializer.Serialize(response),
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.BacktestReports.Add(reportEntity);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("[Backtest] Saved fresh report to cache (WeeksBack: {WeeksBack})", query.WeeksBack);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Backtest] Failed to save report to cache.");
        }

        return response;
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
            "Under 2.5 Goals" => goals < 3,
            "2-3 Goals" => goals == 2 || goals == 3,
            _ => false
        };
    }

    private GetBacktestReportResponse CalculateFinalReport(
        List<SimulationCombo> results,
        List<LeaguePredictionResult> leagueResults,
        List<MarketSampleRow> marketSamples,
        List<HdaSampleRow> hdaSamples,
        List<QualifiedPickRow> qualifiedPicks,
        List<GateOutcomeRow> gateOutcomes,
        List<ShadowPickRow> shadowPicks,
        DateTimeOffset startDate, int weeks, double stake)
    {
        // Group by week, but apply daily dynamic limits (4 on weekends, 1 on weekdays)
        var weeklyGroups = results.GroupBy(r => ISOWeek.GetWeekOfYear(r.Date))
            .Select(g => 
            {
                var dailyLimited = g.GroupBy(x => x.Date.Date)
                                    .SelectMany(dailyGroup => 
                                    {
                                        int dailyTake = (dailyGroup.Key.DayOfWeek == DayOfWeek.Saturday || dailyGroup.Key.DayOfWeek == DayOfWeek.Sunday) ? 4 : 1;
                                        return dailyGroup.OrderByDescending(x => x.AverageConfidence).Take(dailyTake);
                                    }).ToList();
                return new 
                {
                    WeekKey = g.Key,
                    Items = dailyLimited
                };
            }).ToList();

        var finalSimulations = weeklyGroups.SelectMany(g => g.Items).ToList();

        var totalLegs = finalSimulations.Sum(r => r.Legs.Count);
        var correctLegs = finalSimulations.Sum(r => r.Legs.Count(l => l.IsWon));
        
        var totalPredictions = leagueResults.Count;
        var totalHitPredictions = leagueResults.Count(r => r.BttsHit) + leagueResults.Count(r => r.Over25Hit);
        var matchAnalysisAccuracy = totalPredictions > 0 ? (double)totalHitPredictions / (totalPredictions * 2) * 100 : 0;

        var weeklyBreakdown = weeklyGroups
            .Select(g => 
            {
                var minDate = g.Items.Min(x => x.Date);
                var maxDate = g.Items.Max(x => x.Date);
                return new WeeklyBreakdown
                {
                    Week = $"Week {g.WeekKey}",
                    DateRange = $"{minDate:MMM dd} - {maxDate:MMM dd}",
                    TotalCombinations = g.Items.Count,
                    CombinationsWon = g.Items.Count(x => x.IsWon),
                    StakeAmount = Math.Round(g.Items.Sum(x => x.Stake), 2),
                    ProfitLoss = Math.Round(g.Items.Sum(x => x.Return - x.Stake), 2),
                    RoiPercent = Math.Round(g.Items.Sum(x => x.Stake) > 0 ? (g.Items.Sum(x => x.Return - x.Stake) / g.Items.Sum(x => x.Stake)) * 100 : 0, 1)
                };
            }).ToList();

        // Calculate League Accuracy — n on every row, sorted by n desc, low-sample flag
        var leagueAccuracy = leagueResults.GroupBy(l => l.League)
            .Select(g => new LeagueAccuracy
            {
                League = g.Key,
                SampleSize = g.Count(),
                LowSample = g.Count() < LowSampleThreshold,
                BttsAccuracy = Math.Round((double)g.Count(x => x.BttsHit) / g.Count() * 100, 1),
                Over25Accuracy = Math.Round((double)g.Count(x => x.Over25Hit) / g.Count() * 100, 1)
            })
            .OrderByDescending(l => l.SampleSize)
            .ToList();

        var marketMetrics = BuildMarketMetrics(marketSamples, hdaSamples);
        var calibration = BuildCalibration(marketSamples);
        var qualified = BuildQualifiedPicksReport(qualifiedPicks);
        var rulePerformance = BuildRulePerformance(qualifiedPicks);
        var funnel = BuildQualificationFunnel(gateOutcomes);
        var shadow = BuildShadowCohorts(shadowPicks);

        var totalStaked = finalSimulations.Sum(r => r.Stake);
        var totalReturned = finalSimulations.Sum(r => r.Return);
        var roi = totalStaked > 0 ? ((totalReturned - totalStaked) / totalStaked) * 100 : 0;

        return new GetBacktestReportResponse
        {
            Summary = new BacktestSummary
            {
                StartDate = startDate,
                TotalRoi = Math.Round(roi, 1),
                TotalStaked = Math.Round(totalStaked, 2),
                TotalReturned = Math.Round(totalReturned, 2),
                CombinationAccuracy = Math.Round(finalSimulations.Count > 0 ? (double)finalSimulations.Count(r => r.IsWon) / finalSimulations.Count * 100 : 0, 1),
                WinRate = Math.Round(finalSimulations.Count > 0 ? (double)finalSimulations.Count(r => r.IsWon) / finalSimulations.Count * 100 : 0, 1),
                CombosTotal = finalSimulations.Count,
                CombosWon = finalSimulations.Count(r => r.IsWon),
                MatchAnalysisAccuracy = Math.Round(matchAnalysisAccuracy, 1),
                TotalLegs = totalLegs,
                CorrectLegs = correctLegs
            },
            WeeklyBreakdown = weeklyBreakdown,
            LeagueAccuracy = leagueAccuracy,
            MarketMetrics = marketMetrics,
            Calibration = calibration,
            QualifiedPicks = qualified,
            RulePerformance = rulePerformance,
            QualificationFunnel = funnel,
            ShadowCohorts = shadow
        };
    }

    /// <summary>
    /// Shadow cohort performance: hit rate + would-be flat ROI of picks the
    /// price gates rejected. Per cohort×market with an ALL row, plus per-league
    /// rows. Measurement only — these never were and never become real picks.
    /// </summary>
    private static List<ShadowCohortRow> BuildShadowCohorts(List<ShadowPickRow> picks)
    {
        ShadowCohortRow Build(string cohort, string market, string league, List<ShadowPickRow> rows)
        {
            var returned = rows.Sum(p => p.Won ? p.Odds : 0);
            var withEv = rows.Where(p => p.Ev is not null).ToList();
            return new ShadowCohortRow
            {
                Cohort = cohort,
                Market = market,
                League = league,
                Count = rows.Count,
                Hits = rows.Count(p => p.Won),
                HitRate = rows.Count > 0 ? Math.Round((double)rows.Count(p => p.Won) / rows.Count * 100, 1) : 0,
                AvgOdds = rows.Count > 0 ? Math.Round(rows.Average(p => p.Odds), 2) : 0,
                AvgEv = withEv.Count > 0 ? Math.Round(withEv.Average(p => p.Ev!.Value), 4) : 0,
                WouldBeRoiPercent = rows.Count > 0
                    ? Math.Round((returned - rows.Count) / rows.Count * 100, 1) : 0
            };
        }

        var result = new List<ShadowCohortRow>();
        foreach (var group in picks.GroupBy(p => (p.Cohort, p.Market)))
        {
            var rows = group.ToList();
            result.Add(Build(group.Key.Cohort, group.Key.Market, "ALL", rows));
            result.AddRange(rows.GroupBy(p => p.League)
                .OrderByDescending(g => g.Count())
                .Select(g => Build(group.Key.Cohort, group.Key.Market, g.Key, g.ToList())));
        }

        return result
            .OrderBy(r => r.Cohort).ThenBy(r => r.Market)
            .ThenByDescending(r => r.League == "ALL").ThenByDescending(r => r.Count)
            .ToList();
    }

    /// <summary>
    /// Value-gate funnel per market: where fixtures dropped out (no odds,
    /// MinOdds floor, EV, probability floor, vetoes, confirms) vs qualified.
    /// The below_min_odds count answers how many +EV picks the floors reject.
    /// </summary>
    private static List<QualificationFunnelRow> BuildQualificationFunnel(List<GateOutcomeRow> outcomes)
    {
        return outcomes
            .GroupBy(o => o.Market)
            .Select(g => new QualificationFunnelRow
            {
                Market = g.Key,
                Total = g.Count(),
                AnalysisOnlyNoOdds = g.Count(o => o.Outcome == "analysis_only_no_odds"),
                BelowMinOdds = g.Count(o => o.Outcome == "below_min_odds"),
                BelowMinEdge = g.Count(o => o.Outcome == "below_min_edge"),
                BelowProbabilityFloor = g.Count(o => o.Outcome == "below_probability_floor"),
                Vetoed = g.Count(o => o.Outcome == "vetoed"),
                InsufficientConfirms = g.Count(o => o.Outcome == "insufficient_confirms"),
                Qualified = g.Count(o => o.Outcome == "qualified")
            })
            .OrderBy(r => r.Market)
            .ToList();
    }

    /// <summary>
    /// Per-rule performance among qualified picks: hit rate of picks where the
    /// rule fired vs qualified picks of the same market where it did not.
    /// This tells us which rules earn their place.
    /// </summary>
    private static List<RulePerformanceRow> BuildRulePerformance(List<QualifiedPickRow> picks)
    {
        var rows = new List<RulePerformanceRow>();

        foreach (var marketGroup in picks.GroupBy(p => p.Market))
        {
            var marketPicks = marketGroup.ToList();
            var ruleIds = marketPicks.SelectMany(p => p.FiredRules).Distinct().OrderBy(r => r);

            foreach (var ruleId in ruleIds)
            {
                var withRule = marketPicks.Where(p => p.FiredRules.Contains(ruleId)).ToList();
                var withoutRule = marketPicks.Where(p => !p.FiredRules.Contains(ruleId)).ToList();

                rows.Add(new RulePerformanceRow
                {
                    Market = marketGroup.Key,
                    RuleId = ruleId,
                    PicksWith = withRule.Count,
                    HitRateWith = withRule.Count > 0
                        ? Math.Round((double)withRule.Count(p => p.Won) / withRule.Count * 100, 1) : 0,
                    PicksWithout = withoutRule.Count,
                    HitRateWithout = withoutRule.Count > 0
                        ? Math.Round((double)withoutRule.Count(p => p.Won) / withoutRule.Count * 100, 1) : 0
                });
            }
        }

        return rows.OrderBy(r => r.Market).ThenByDescending(r => r.PicksWith).ToList();
    }

    // ── Report sections (Task 2 a-c) ─────────────────────────────────────────

    private static readonly (double Lower, double Upper)[] CalibrationRanges =
        [(0.50, 0.55), (0.55, 0.60), (0.60, 0.65), (0.65, 1.00)];

    /// <summary>
    /// 2-3 Goals probabilities peak near 40-50% (its qualification threshold is
    /// 45%) — the standard 50%+ buckets would be empty for it.
    /// </summary>
    private static readonly (double Lower, double Upper)[] Goals23CalibrationRanges =
        [(0.35, 0.40), (0.40, 0.45), (0.45, 0.50), (0.50, 0.55), (0.55, 1.00)];

    /// <summary>Draw probabilities live in the 20-35% band.</summary>
    private static readonly (double Lower, double Upper)[] DrawCalibrationRanges =
        [(0.20, 0.25), (0.25, 0.30), (0.30, 0.35), (0.35, 1.00)];

    private static readonly string[] BinaryMarkets = ["btts", "over25", "goals_2_3", "match_winner", "draw"];

    private static List<MarketMetrics> BuildMarketMetrics(
        List<MarketSampleRow> marketSamples, List<HdaSampleRow> hdaSamples)
    {
        var metrics = new List<MarketMetrics>();

        foreach (var market in BinaryMarkets.Where(m => m != "match_winner"))
        {
            var rows = marketSamples.Where(r => r.Market == market).ToList();
            var samples = ToHarnessSamples(marketSamples, market);
            metrics.Add(new MarketMetrics
            {
                Market = market,
                SampleSize = samples.Count,
                BrierScore = Math.Round(EvaluationHarness.Brier(samples), 4),
                LogLoss = Math.Round(EvaluationHarness.LogLoss(samples), 4),
                ValidOddsPct = rows.Count > 0
                    ? Math.Round((double)rows.Count(r => r.OddsValid) / rows.Count * 100, 1) : 0
            });
        }

        // 1X2 as a proper three-way multiclass metric.
        var hda = hdaSamples.Select(s => (s.Probabilities, s.ActualIndex)).ToList();
        var winnerRows = marketSamples.Where(r => r.Market == "match_winner").ToList();
        metrics.Add(new MarketMetrics
        {
            Market = "1x2",
            SampleSize = hda.Count,
            BrierScore = Math.Round(EvaluationHarness.MulticlassBrier(hda), 4),
            LogLoss = Math.Round(EvaluationHarness.MulticlassLogLoss(hda), 4),
            ValidOddsPct = winnerRows.Count > 0
                ? Math.Round((double)winnerRows.Count(r => r.OddsValid) / winnerRows.Count * 100, 1) : 0
        });

        return metrics;
    }

    private static List<MarketCalibration> BuildCalibration(List<MarketSampleRow> marketSamples)
    {
        return BinaryMarkets.Select(market =>
        {
            var ranges = market switch
            {
                "goals_2_3" => Goals23CalibrationRanges,
                "draw" => DrawCalibrationRanges,
                _ => CalibrationRanges
            };
            var buckets = EvaluationHarness.CalibrationForRanges(
                ToHarnessSamples(marketSamples, market), ranges);

            return new MarketCalibration
            {
                Market = market,
                Buckets = buckets.Select(b => new CalibrationBucketRow
                {
                    Range = b.Upper >= 1.0 ? $"{b.Lower:P0}+" : $"{b.Lower:P0}-{b.Upper:P0}",
                    SampleSize = b.Count,
                    PredictedAvg = Math.Round(b.MeanPredicted, 4),
                    ActualHitRate = Math.Round(b.ObservedRate, 4)
                }).ToList()
            };
        }).ToList();
    }

    private QualifiedPicksReport BuildQualifiedPicksReport(List<QualifiedPickRow> picks)
    {
        var opt = confluenceOptions.Value;
        double ThresholdOf(string market) => market switch
        {
            "btts" => opt.BttsMinProbability,
            "over25" => opt.Over25MinProbability,
            "goals_2_3" => opt.Goals23MinProbability,
            "match_winner" => opt.WinnerMinProbability,
            "low_scoring" => opt.Under25MinProbability,
            "draw" => opt.DrawMinProbability,
            _ => 0
        };

        QualifiedMarketRow BuildRow(string market, List<QualifiedPickRow> rows)
        {
            // Odds are pre-sanitized: null = missing OR outside the 1.01-15.0 guard.
            var withOdds = rows.Where(p => p.Odds is not null).ToList();
            var staked = withOdds.Count;
            var returned = withOdds.Sum(p => p.Won ? p.Odds!.Value : 0);

            // Kelly ROI: stake = fractional Kelly (bankroll share) per pick.
            var kellyPicks = withOdds.Where(p => p.KellyStake is > 0).ToList();
            var kellyStaked = kellyPicks.Sum(p => p.KellyStake!.Value);
            var kellyReturned = kellyPicks.Sum(p => p.Won ? p.KellyStake!.Value * p.Odds!.Value : 0);

            var withEv = rows.Where(p => p.Ev is not null).ToList();

            return new QualifiedMarketRow
            {
                Market = market,
                Count = rows.Count,
                Hits = rows.Count(p => p.Won),
                HitRate = rows.Count > 0 ? Math.Round((double)rows.Count(p => p.Won) / rows.Count * 100, 1) : 0,
                AvgOdds = withOdds.Count > 0 ? Math.Round(withOdds.Average(p => p.Odds!.Value), 2) : 0,
                RoiPercent = staked > 0 ? Math.Round((returned - staked) / staked * 100, 1) : 0,
                KellyRoiPercent = kellyStaked > 0
                    ? Math.Round((kellyReturned - kellyStaked) / kellyStaked * 100, 1) : 0,
                AvgEv = withEv.Count > 0 ? Math.Round(withEv.Average(p => p.Ev!.Value), 4) : 0,
                QualificationThreshold = ThresholdOf(market),
                ValidOddsPct = rows.Count > 0
                    ? Math.Round((double)withOdds.Count / rows.Count * 100, 1) : 0
            };
        }

        var overall = BuildRow("all", picks);
        var withOddsAll = picks.Where(p => p.Odds is not null).ToList();

        return new QualifiedPicksReport
        {
            Count = picks.Count,
            Hits = picks.Count(p => p.Won),
            HitRate = overall.HitRate,
            AvgOdds = overall.AvgOdds,
            TotalStaked = withOddsAll.Count,
            TotalReturned = Math.Round(withOddsAll.Sum(p => p.Won ? p.Odds!.Value : 0), 2),
            RoiPercent = overall.RoiPercent,
            KellyRoiPercent = overall.KellyRoiPercent,
            AvgEv = overall.AvgEv,
            PerMarket = picks.GroupBy(p => p.Market)
                .Select(g => BuildRow(g.Key, g.ToList()))
                .OrderByDescending(r => r.Count)
                .ToList()
        };
    }

    private static List<PredictionSample> ToHarnessSamples(List<MarketSampleRow> rows, string market) =>
        rows.Where(r => r.Market == market)
            .Select(r => new PredictionSample(r.Market, 0, r.Probability, r.Outcome))
            .ToList();

    private class SimulationCombo
    {
        public DateTime Date { get; set; }
        public double Odds { get; set; }
        public bool IsWon { get; set; }
        public double Stake { get; set; }
        public double Return { get; set; }
        public double AverageConfidence { get; set; }
        public List<LegResult> Legs { get; set; } = [];
    }

    private class LegResult
    {
        public bool IsWon { get; set; }
    }

    private class LeaguePredictionResult
    {
        public string League { get; set; } = "";
        public bool BttsHit { get; set; }
        public bool Over25Hit { get; set; }
    }
}
