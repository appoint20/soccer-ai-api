
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure;
using soccer_gpt_infrastructure.Services;

namespace TestProject;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Contains("--verify-model"))
        {
            PoissonVerifier.Run();
            return;
        }

        Console.WriteLine("=== Backtesting Runner Starting ===");
        
        // 1. Setup Dependency Injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning)); // Reduce log noise
        services.AddInfrastructure(); 

        var provider = services.BuildServiceProvider();

        // 2. Resolve Services
        var excelService = provider.GetRequiredService<IHistoricalDataRepository>();
        var advancedStatsService = provider.GetRequiredService<IAdvancedStatsService>();
        var trapService = provider.GetRequiredService<ITrapDetectionService>();

        // 3. Load Data
        Console.Write("Loading Historical Data...");
        var allMatches = await excelService.GetAllMatchesAsync();
        Console.WriteLine($" Done. Loaded {allMatches.Count} matches.");

        if (allMatches.Count == 0)
        {
            Console.WriteLine("No data found.");
            return;
        }

        // 4. Define Backtest Period (Last 10 Weeks)
        var lastMatchDate = allMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-70); 
        
        var testSet = allMatches
            .Where(m => m.Date >= startDate)
            .OrderBy(m => m.Date)
            .ToList();

        Console.WriteLine($"\nBacktesting Period: {startDate:yyyy-MM-dd} to {lastMatchDate:yyyy-MM-dd}");
        Console.WriteLine($"Total Matches to Predict: {testSet.Count}");

        // 5. Run Validation Loop
        int total = 0;
        int correct1X2 = 0;
        int correctOver25 = 0;
        int correctBTTS = 0;
        
        // Trap Tracking
        int boreDrawSignals = 0; int boreDrawSuccess = 0;
        int oddsTrapSignals = 0; int oddsTrapSuccess = 0;

        foreach (var match in testSet)
        {
            var historyDateLimit = match.Date;
            var pastHistory = allMatches.Where(m => m.Date < historyDateLimit).ToList();

            if (pastHistory.Count < 500) continue; 

            try 
            {
                var result = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, pastHistory);
                var probs = result.Probabilities;
                
                // --- Trap Detection Integration ---
                var upcomingDto = new soccer_gpt_application.Models.UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    League = match.League, // Property is called League in Historical DTO
                    LeagueName = match.League, 
                    Odds = new soccer_gpt_application.Models.MatchOdds 
                    { 
                        HomeWin = match.Odds?.HomeWin ?? 0, 
                        Draw = match.Odds?.Draw ?? 0, 
                        AwayWin = match.Odds?.AwayWin ?? 0 
                    }
                };
                
                var traps = trapService.AnalyzeTraps(upcomingDto, result);
                
                // Check Trap Success
                bool isBoreDrawTrap = traps.Any(t => t.Contains("Bore Draw"));
                bool isOddsTrap = traps.Any(t => t.Contains("Odds Trap"));

                if (isBoreDrawTrap)
                {
                    boreDrawSignals++;
                    // Success if Under 2.5 (Low scoring) OR Under 1.5? 
                    // Detector says "Probability of low goals (< 1.5)". So let's check < 2.5 as a "Safe" validation or < 1.5 strict.
                    // Let's use Under 2.5 as the "Avoid Over" success metric.
                    if ((match.FTHG + match.FTAG) < 3) boreDrawSuccess++;
                }

                if (isOddsTrap)
                {
                    oddsTrapSignals++;
                    // Trap means Favorite should NOT win.
                    // Assuming Odds Trap implies Home Trap (logic in detector usually checks Home Fav).
                    // If match.FTR is NOT "H", then Trap was correct.
                    if (match.FTR != "H") oddsTrapSuccess++;
                }

                total++;

                // 1X2 Check (Highest Prob)
                string predictedFTR = "D";
                if (probs.HomeWin > probs.Draw && probs.HomeWin > probs.AwayWin) predictedFTR = "H";
                else if (probs.AwayWin > probs.HomeWin && probs.AwayWin > probs.Draw) predictedFTR = "A";
                
                if (match.FTR == predictedFTR) correct1X2++;

                // Over 2.5 Check (> 50%)
                bool predOver25 = probs.Over25 > 0.50;
                bool actualOver25 = (match.FTHG + match.FTAG) > 2.5;
                if (predOver25 == actualOver25) correctOver25++;

                // BTTS Check (> 50%)
                bool predBTTS = probs.BTTS > 0.50;
                bool actualBTTS = (match.FTHG > 0 && match.FTAG > 0);
                if (predBTTS == actualBTTS) correctBTTS++;

                if (total % 50 == 0) Console.Write(".");
            }
            catch (Exception)
            {
                // Ignore data errors
            }
        }

        Console.WriteLine("\n\n=== Backtest Results (Last 10 Weeks) ===");
        Console.WriteLine($"Matches Analyzed: {total}");
        Console.WriteLine($"1X2 Accuracy:  {(double)correct1X2/total:P2}");
        Console.WriteLine($"O2.5 Accuracy: {(double)correctOver25/total:P2}");
        Console.WriteLine($"BTTS Accuracy: {(double)correctBTTS/total:P2}");
        
        Console.WriteLine($"\n--- Trap Detector Efficiency ---");
        Console.WriteLine($"Bore Draw Traps: {boreDrawSignals} | Correct (Under 2.5): {(boreDrawSignals > 0 ? (double)boreDrawSuccess/boreDrawSignals : 0):P2}");
        Console.WriteLine($"Odds Traps:      {oddsTrapSignals}  | Correct (Fav Failed): {(oddsTrapSignals > 0 ? (double)oddsTrapSuccess/oddsTrapSignals : 0):P2}");
    }
}
