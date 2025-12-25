using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_infrastructure;

namespace TestProject;

public class UnfilteredBacktest
{
    public static async Task Run()
    {
        Console.WriteLine("=== Unfiltered Poisson Backtest (15 Weeks) ===\n");
        Console.WriteLine("Testing RAW Poisson predictions with NO filters\n");
        
        // Setup DI
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
        
        var dataRepo = provider.GetRequiredService<IHistoricalDataRepository>();
        var advancedStats = provider.GetRequiredService<IAdvancedStatsService>();
        
        // Load data
        Console.WriteLine("Loading historical data...");
        var allMatches = await dataRepo.GetAllMatchesAsync();
        Console.WriteLine($"Loaded {allMatches.Count} matches.\n");
        
        // Define test period (last 15 weeks)
        var lastMatchDate = allMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-105); // ~15 weeks
        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        Console.WriteLine($"Test period: {startDate:yyyy-MM-dd} to {lastMatchDate:yyyy-MM-dd}");
        Console.WriteLine($"Total matches: {testSet.Count}\n");
        
        // Track predictions
        int totalMatches = 0;
        int over25Correct = 0, over25Total = 0;
        int bttsCorrect = 0, bttsTotal = 0;
        int homeWinCorrect = 0, homeWinTotal = 0;
        int drawCorrect = 0, drawTotal = 0;
        int awayWinCorrect = 0, awayWinTotal = 0;
        
        foreach (var match in testSet)
        {
            var pastHistory = allMatches
                .Where(m => m.Date < match.Date)
                .OrderByDescending(m => m.Date)
                .Take(1000)
                .ToList();
            
            if (pastHistory.Count < 20) continue;
            
            try
            {
                var analytics = await advancedStats.CalculateAnalyticsAsync(
                    match.HomeTeam, match.AwayTeam, pastHistory, match.League, null);
                
                var probs = analytics.Probabilities;
                
                // Actual results
                var totalGoals = match.FTHG + match.FTAG;
                var actualOver25 = totalGoals > 2.5;
                var actualBTTS = match.FTHG > 0 && match.FTAG > 0;
                var actualResult = match.FTR; // "H", "D", or "A"
                
                // Check predictions (predict the highest probability)
                var predictedOver25 = probs.Over25 > 0.5;
                var predictedBTTS = probs.BTTS > 0.5;
                
                // 1X2 prediction
                var maxProb = Math.Max(probs.HomeWin, Math.Max(probs.Draw, probs.AwayWin));
                string predicted1X2;
                if (maxProb == probs.HomeWin) predicted1X2 = "H";
                else if (maxProb == probs.Draw) predicted1X2 = "D";
                else predicted1X2 = "A";
                
                // Track results
                totalMatches++;
                
                over25Total++;
                if (predictedOver25 == actualOver25) over25Correct++;
                
                bttsTotal++;
                if (predictedBTTS == actualBTTS) bttsCorrect++;
                
                if (predicted1X2 == "H")
                {
                    homeWinTotal++;
                    if (actualResult == "H") homeWinCorrect++;
                }
                else if (predicted1X2 == "D")
                {
                    drawTotal++;
                    if (actualResult == "D") drawCorrect++;
                }
                else
                {
                    awayWinTotal++;
                    if (actualResult == "A") awayWinCorrect++;
                }
                
                if (totalMatches % 200 == 0)
                {
                    Console.WriteLine($"Processed: {totalMatches}/{testSet.Count}");
                }
            }
            catch
            {
                // Skip errors
            }
        }
        
        // Results
        Console.WriteLine("\n========================================");
        Console.WriteLine("UNFILTERED POISSON ACCURACY");
        Console.WriteLine("========================================\n");
        
        Console.WriteLine($"Total Matches Analyzed: {totalMatches}\n");
        
        Console.WriteLine("MARKET ACCURACY (Predicting most likely outcome):");
        Console.WriteLine("─────────────────────────────────────");
        Console.WriteLine($"Over 2.5 Goals:  {over25Correct}/{over25Total} = {(double)over25Correct/over25Total:P1}");
        Console.WriteLine($"BTTS (Yes/No):   {bttsCorrect}/{bttsTotal} = {(double)bttsCorrect/bttsTotal:P1}");
        Console.WriteLine();
        
        Console.WriteLine("1X2 PREDICTIONS:");
        Console.WriteLine("─────────────────────────────────────");
        Console.WriteLine($"Home Win predicted: {homeWinTotal} ({(homeWinTotal > 0 ? (double)homeWinCorrect/homeWinTotal : 0):P1} accurate)");
        Console.WriteLine($"Draw predicted:     {drawTotal} ({(drawTotal > 0 ? (double)drawCorrect/drawTotal : 0):P1} accurate)");
        Console.WriteLine($"Away Win predicted: {awayWinTotal} ({(awayWinTotal > 0 ? (double)awayWinCorrect/awayWinTotal : 0):P1} accurate)");
        
        var total1X2Correct = homeWinCorrect + drawCorrect + awayWinCorrect;
        var total1X2 = homeWinTotal + drawTotal + awayWinTotal;
        Console.WriteLine($"\nOverall 1X2 Accuracy: {total1X2Correct}/{total1X2} = {(double)total1X2Correct/total1X2:P1}");
        
        Console.WriteLine("\n========================================\n");
        
        Console.WriteLine("INTERPRETATION:");
        Console.WriteLine("This shows raw Poisson model accuracy WITHOUT:");
        Console.WriteLine("  - Confidence thresholds");
        Console.WriteLine("  - League filters");
        Console.WriteLine("  - Odds requirements");
        Console.WriteLine("  - H2H adjustments");
        Console.WriteLine("\nThese are base probabilities for ALL matches.");
    }
}
