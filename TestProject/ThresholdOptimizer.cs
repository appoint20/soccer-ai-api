using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;
using soccer_gpt_infrastructure.Services.Decision;
using soccer_gpt_infrastructure.Services.ML;
using Microsoft.Extensions.Logging;
using System.Text;

namespace TestProject;

public class ThresholdOptimizer
{
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IAdvancedStatsService _advancedStats;
    private readonly IMlPredictionService _mlService;
    private readonly IH2HReliabilityService _h2hService;
    private readonly ITrapDetectionService _trapService;
    private readonly ILogger<ThresholdOptimizer> _logger;

    public ThresholdOptimizer(
        IHistoricalDataRepository dataRepo,
        IAdvancedStatsService advancedStats,
        IMlPredictionService mlService,
        IH2HReliabilityService h2hService,
        ITrapDetectionService trapService,
        ILogger<ThresholdOptimizer> logger)
    {
        _dataRepo = dataRepo;
        _advancedStats = advancedStats;
        _mlService = mlService;
        _h2hService = h2hService;
        _trapService = trapService;
        _logger = logger;
    }

    public async Task OptimizeAllLeagues()
    {
        var leagues = new[] { "E0", "E1", "D1", "F1", "F2", "SP1", "I1", "I2" };
        
        // Threshold ranges to test
        var over25Thresholds = new[] { 0.55, 0.58, 0.60, 0.62, 0.65, 0.68, 0.70 };
        var bttsThresholds = new[] { 0.52, 0.55, 0.58, 0.60, 0.62, 0.65, 0.68 };

        var allResults = new List<OptimizationResult>();

        Console.WriteLine("=== League-Specific Threshold Optimization ===\n");
        Console.WriteLine("Testing threshold combinations for each league...\n");

        foreach (var league in leagues)
        {
            Console.WriteLine($"Optimizing {league}...");
            var leagueResults = await OptimizeLeague(league, over25Thresholds, bttsThresholds);
            allResults.AddRange(leagueResults);
            
            // Find best for this league
            var best = leagueResults
                .Where(r => r.WinRate >= 0.52 && r.ROI >= 0.05)
                .OrderByDescending(r => r.TotalBets)
                .FirstOrDefault();

            if (best != null)
            {
                Console.WriteLine($"  Best: Over25={best.Over25Threshold:P0}, BTTS={best.BttsThreshold:P0}");
                Console.WriteLine($"  → {best.TotalBets} bets, {best.WinRate:P1} win rate, {best.ROI:P1} ROI\n");
            }
            else
            {
                Console.WriteLine($"  No profitable configuration found!\n");
            }
        }

        // Generate summary report
        await GenerateReport(allResults, leagues);
    }

    private async Task<List<OptimizationResult>> OptimizeLeague(
        string league,
        double[] over25Thresholds,
        double[] bttsThresholds)
    {
        var results = new List<OptimizationResult>();

        // Load historical data for this league
        var allMatches = await _dataRepo.GetAllMatchesAsync();
        var leagueMatches = allMatches.Where(m => m.League == league).ToList();

        // Define test period (last 15 weeks)
        var lastMatchDate = leagueMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-105); // ~15 weeks
        var testSet = leagueMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();

        _logger.LogInformation($"Testing {league}: {testSet.Count} matches in test period");

        // Test each combination
        foreach (var over25T in over25Thresholds)
        {
            foreach (var bttsT in bttsThresholds)
            {
                var result = await TestThresholds(league, testSet, over25T, bttsT, allMatches);
                results.Add(result);
            }
        }

        return results;
    }

    private async Task<OptimizationResult> TestThresholds(
        string league,
        List<HistoricalMatchDto> testSet,
        double over25Threshold,
        double bttsThreshold,
        List<HistoricalMatchDto> allHistory)
    {
        var result = new OptimizationResult
        {
            League = league,
            Over25Threshold = over25Threshold,
            BttsThreshold = bttsThreshold
        };

        int totalBets = 0;
        int wins = 0;
        double totalStake = 0;
        double totalReturn = 0;

        foreach (var match in testSet)
        {
            // Get past history for this match
            var pastHistory = allHistory
                .Where(m => m.Date < match.Date)
                .OrderByDescending(m => m.Date)
                .Take(1000)
                .ToList();

            if (pastHistory.Count < 20)  continue; // Need minimum history

            try
            {
                // Generate predictions
                var analytics = await _advancedStats.CalculateAnalyticsAsync(
                    match.HomeTeam, match.AwayTeam, pastHistory, match.League, null);

                var decision = analytics.Decision;

                // Apply custom thresholds
                var selectedMarket = EvaluateWithCustomThresholds(
                    analytics.Probabilities,
                    pastHistory,
                    match.HomeTeam,
                    match.AwayTeam,
                    over25Threshold,
                    bttsThreshold,
                    match.Odds);

                if (selectedMarket == null) continue;

                totalBets++;
                totalStake += 10;

                // Check outcome
                var won = CheckBetOutcome(selectedMarket, match);
                if (won)
                {
                    wins++;
                    var odds = GetMarketOdds(selectedMarket, match.Odds);
                    totalReturn += 10 * odds;
                }
            }
            catch
            {
                // Skip matches with errors
                continue;
            }
        }

        result.TotalBets = totalBets;
        result.Wins = wins;
        result.WinRate = totalBets > 0 ? (double)wins / totalBets : 0;
        result.TotalStake = totalStake;
        result.TotalReturn = totalReturn;
        result.Profit = totalReturn - totalStake;
        result.ROI = totalStake > 0 ? (totalReturn - totalStake) / totalStake : 0;

        return result;
    }

    private string? EvaluateWithCustomThresholds(
        MatchProbabilitiesDto probs,
        List<HistoricalMatchDto> history,
        string homeTeam,
        string awayTeam,
        double over25Threshold,
        double bttsThreshold,
        MatchOddsDto? odds)
    {
        var candidates = new List<(string Market, double Confidence, double Odds)>();

        // Over 2.5
        var over25H2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "Over 2.5 Goals");
        var over25Conf = probs.Over25 * over25H2H.Multiplier;
        if (over25Conf >= over25Threshold && odds?.Over25 >= 1.80m)
        {
            candidates.Add(("Over 2.5 Goals", over25Conf, (double)odds.Over25));
        }

        // BTTS
        var bttsH2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "BTTS Yes");
        var bttsConf = probs.BTTS * bttsH2H.Multiplier;
        if (bttsConf >= bttsThreshold && odds?.BttsYes >= 1.85m)
        {
            candidates.Add(("BTTS Yes", bttsConf, (double)odds.BttsYes));
        }

        if (candidates.Count == 0) return null;

        // Select best EV (with BTTS bonus)
        double bestEV = -1;
        string? bestMarket = null;

        foreach (var (market, conf, marketOdds) in candidates)
        {
            var ev = (conf * marketOdds) - 1;
            if (market == "BTTS Yes") ev += 0.05; // BTTS bonus

            if (ev > bestEV)
            {
                bestEV = ev;
                bestMarket = market;
            }
        }

        return bestMarket;
    }

    private bool CheckBetOutcome(string market, HistoricalMatchDto match)
    {
        var totalGoals = match.FTHG + match.FTAG;
        
        return market switch
        {
            "Over 2.5 Goals" => totalGoals > 2.5,
            "BTTS Yes" => match.FTHG > 0 && match.FTAG > 0,
            _ => false
        };
    }

    private double GetMarketOdds(string market, MatchOddsDto? odds)
    {
        if (odds == null) return 2.00;

        return market switch
        {
            "Over 2.5 Goals" => (double)odds.Over25,
            "BTTS Yes" => (double)odds.BttsYes,
            _ => 2.00
        };
    }

    private async Task GenerateReport(List<OptimizationResult> allResults, string[] leagues)
    {
        var report = new StringBuilder();
        report.AppendLine("\n========================================");
        report.AppendLine("LEAGUE THRESHOLD OPTIMIZATION RESULTS");
        report.AppendLine("========================================\n");

        // Best configuration per league
        report.AppendLine("RECOMMENDED THRESHOLDS PER LEAGUE:");
        report.AppendLine("─────────────────────────────────────\n");

        var totalBets = 0;
        var totalWins = 0;
        var totalStake = 0.0;
        var totalReturn = 0.0;

        foreach (var league in leagues)
        {
            var leagueResults = allResults.Where(r => r.League == league).ToList();
            var best = leagueResults
                .Where(r => r.WinRate >= 0.52 && r.ROI >= 0.05)
                .OrderByDescending(r => r.TotalBets)
                .FirstOrDefault();

            if (best != null)
            {
                report.AppendLine($"{league}:");
                report.AppendLine($"  Over 2.5: {best.Over25Threshold:P0} | BTTS: {best.BttsThreshold:P0}");
                report.AppendLine($"  {best.TotalBets} bets | {best.WinRate:P1} win rate | {best.ROI:P1} ROI | ${best.Profit:F2} profit");
                report.AppendLine();

                totalBets += best.TotalBets;
                totalWins += best.Wins;
                totalStake += best.TotalStake;
                totalReturn += best.TotalReturn;
            }
            else
            {
                report.AppendLine($"{league}: No profitable configuration found");
                report.AppendLine();
            }
        }

        report.AppendLine("─────────────────────────────────────");
        report.AppendLine($"TOTAL: {totalBets} bets");
        report.AppendLine($"Overall Win Rate: {(totalBets > 0 ? (double)totalWins / totalBets : 0):P1}");
        report.AppendLine($"Overall ROI: {(totalStake > 0 ? (totalReturn - totalStake) / totalStake : 0):P1}");
        report.AppendLine($"Net Profit: ${totalReturn - totalStake:F2}");
        report.AppendLine("========================================\n");

        Console.WriteLine(report.ToString());

        // Save to file
        var reportPath = "/Users/shivm/.gemini/antigravity/brain/6cf8bff2-a8db-4706-9213-fed8cadb7232/threshold_optimization_results.md";
        await File.WriteAllTextAsync(reportPath, report.ToString());
        Console.WriteLine($"Report saved to: {reportPath}");
    }
}

public class OptimizationResult
{
    public string League { get; set; } = "";
    public double Over25Threshold { get; set; }
    public double BttsThreshold { get; set; }
    public int TotalBets { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double TotalStake { get; set; }
    public double TotalReturn { get; set; }
    public double Profit { get; set; }
    public double ROI { get; set; }
}
