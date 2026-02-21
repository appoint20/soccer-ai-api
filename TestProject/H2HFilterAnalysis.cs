using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure;

namespace TestProject;

class H2HFilterAnalysis
{
    public static async Task Run()
    {
        Console.WriteLine("=== H2H Filter Analysis (Pre-Filter Mode) ===\n");
        
        // Setup DI (exactly like main Program.cs)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(sp => 
        {
            var dict = new Dictionary<string, string>
            {
                ["EuropeanFixtures:ApiHost"] = "dummy",
                ["EuropeanFixtures:ApiKey"] = "dummy"
            };
            return new MockConfiguration(dict);
        });
        
        services.AddInfrastructure();
        var provider = services.BuildServiceProvider();
        
        var excelService = provider.GetRequiredService<IHistoricalDataRepository>();
        var h2hService = provider.GetRequiredService<IH2HReliabilityService>();
        
        // Load data
        Console.WriteLine("Loading historical data...");
        var allMatches = await excelService.GetAllMatchesAsync();
        
        var lastMatchDate = allMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-70);
        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        Console.WriteLine($"Test period: {startDate:yyyy-MM-dd} to {lastMatchDate:yyyy-MM-dd}");
        Console.WriteLine($"Total matches: {testSet.Count}\n");
        
        // Track H2H filter results per market
        var markets = new[] { "Over 2.5 Goals", "BTTS Yes", "Home Win", "Away Win" };
        var stats = new Dictionary<string, H2HFilterStats>();
        
        foreach (var market in markets)
        {
            stats[market] = new H2HFilterStats();
        }
        
        // Analyze each match
        foreach (var match in testSet)
        {
            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
            if (pastHistory.Count < 500) continue;
            
            // Evaluate each market with H2H
            EvaluateMarket(stats["Over 2.5 Goals"], match, pastHistory, h2hService, "Over 2.5 Goals",
                m => (m.FTHG + m.FTAG) > 2.5);
            
            EvaluateMarket(stats["BTTS Yes"], match, pastHistory, h2hService, "BTTS Yes",
                m => m.FTHG > 0 && m.FTAG > 0);
            
            EvaluateMarket(stats["Home Win"], match, pastHistory, h2hService, "Home Win",
                m => m.FTR == "H");
            
            EvaluateMarket(stats["Away Win"], match, pastHistory, h2hService, "Away Win",
                m => m.FTR == "A");
        }
        
        // Print results
        Console.WriteLine("\n=== H2H PRE-FILTER ANALYSIS ===\n");
        
        foreach (var market in markets)
        {
            var s = stats[market];
            Console.WriteLine($"--- {market} ---");
            Console.WriteLine($"Total Matches:     {s.Total}");
            Console.WriteLine($"H2H Allowed:       {s.Allowed} ({(double)s.Allowed/s.Total:P1})");
            Console.WriteLine($"H2H Dampened:      {s.Dampened} ({(double)s.Dampened/s.Total:P1})");
            Console.WriteLine($"H2H Rejected:      {s.Rejected} ({(double)s.Rejected/s.Total:P1})");
            Console.WriteLine();
            
            if (s.Allowed > 0)
            {
                Console.WriteLine($"  Allowed Accuracy:  {(double)s.AllowedCorrect/s.Allowed:P1} ({s.AllowedCorrect}/{s.Allowed})");
                Console.WriteLine($"  Allowed ROI:       {CalculateROI(s.AllowedCorrect, s.Allowed):F1}%");
            }
            
            if (s.Dampened > 0)
            {
                Console.WriteLine($"  Dampened Accuracy: {(double)s.DampenedCorrect/s.Dampened:P1} ({s.DampenedCorrect}/{s.Dampened})");
                Console.WriteLine($"  Dampened ROI:      {CalculateROI(s.DampenedCorrect, s.Dampened):F1}%");
            }
            
            if (s.Rejected > 0)
            {
                Console.WriteLine($"  Rejected Accuracy: {(double)s.RejectedCorrect/s.Rejected:P1} ({s.RejectedCorrect}/{s.Rejected})");
                Console.WriteLine($"  Rejected ROI:      {CalculateROI(s.RejectedCorrect, s.Rejected):F1}%");
            }
            
            Console.WriteLine();
        }
        
        // Summary
        Console.WriteLine("\n=== SUMMARY ===");
        Console.WriteLine("If we bet on ALL matches H2H ALLOWS (ignoring probability thresholds):\n");
        
        foreach (var market in markets)
        {
            var s = stats[market];
            if (s.Allowed > 0)
            {
                double winRate = (double)s.AllowedCorrect / s.Allowed;
                double roi = CalculateROI(s.AllowedCorrect, s.Allowed);
                string verdict = roi > 0 ? "✅ PROFITABLE" : "❌ LOSING";
                Console.WriteLine($"{market,-20} {s.Allowed,4} bets | {winRate:P1} win rate | {roi,+6:F1}% ROI {verdict}");
            }
        }
    }
    
    private static void EvaluateMarket(
        H2HFilterStats stats,
        HistoricalMatchDto match,
        List<HistoricalMatchDto> history,
        IH2HReliabilityService h2hService,
        string market,
        Func<HistoricalMatchDto, bool> isCorrect)
    {
        stats.Total++;
        
        var h2hResult = h2hService.Evaluate(match.HomeTeam, match.AwayTeam, history, market);
        bool correct = isCorrect(match);
        
        switch (h2hResult.Decision)
        {
            case H2HDecision.Allow:
                stats.Allowed++;
                if (correct) stats.AllowedCorrect++;
                break;
            case H2HDecision.Dampened:
                stats.Dampened++;
                if (correct) stats.DampenedCorrect++;
                break;
            case H2HDecision.Reject:
                stats.Rejected++;
                if (correct) stats.RejectedCorrect++;
                break;
        }
    }
    
    private static double CalculateROI(int wins, int total)
    {
        // Assuming 2.0 odds
        double stake = total * 10.0;
        double returns = wins * 20.0;
        return ((returns - stake) / stake) * 100;
    }
}

class H2HFilterStats
{
    public int Total;
    public int Allowed;
    public int AllowedCorrect;
    public int Dampened;
    public int DampenedCorrect;
    public int Rejected;
    public int RejectedCorrect;
}
