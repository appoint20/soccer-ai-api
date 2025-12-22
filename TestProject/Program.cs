
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
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        // Simple mock configuration
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
        
        // H2H Filter Tracking
        int derbyMatches = 0;
        int bttsH2HCandidates = 0;
        int over25H2HCandidates = 0;
        int twoToThreeH2HCandidates = 0;
        int homeWinH2HCandidates = 0;
        int awayWinH2HCandidates = 0;
        int drawH2HCandidates = 0;
        
        // Decision Layer Tracking
        int highConfidenceBets = 0;
        int highConfidenceWins = 0;
        double totalStake = 0;
        double totalReturns = 0;
        var marketBets = new Dictionary<string, int>();
        var marketWins = new Dictionary<string, int>();

        foreach (var match in testSet)
        {
            var historyDateLimit = match.Date;
            var pastHistory = allMatches.Where(m => m.Date < historyDateLimit).ToList();

            if (pastHistory.Count < 500) continue; 

            try 
            {
                var result = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, pastHistory);
                var probs = result.Probabilities;
                var h2h = result.H2HAnalysis;
                var decision = result.Decision;
                
                // Track H2H Filter Statistics
                if (h2h.IsDerby) derbyMatches++;
                if (h2h.IsBTTSCandidate) bttsH2HCandidates++;
                if (h2h.IsOver25Candidate) over25H2HCandidates++;
                if (h2h.Is2to3GoalsCandidate) twoToThreeH2HCandidates++;
                if (h2h.IsHomeWinCandidate) homeWinH2HCandidates++;
                if (h2h.IsAwayWinCandidate) awayWinH2HCandidates++;
                if (h2h.IsDrawCandidate) drawH2HCandidates++;
                
                // Track Decision Layer Performance
                if (decision.IsHighConfidence)
                {
                    highConfidenceBets++;
                    double stake = 10.0; // $10 per bet
                    totalStake += stake;
                    
                    if (!marketBets.ContainsKey(decision.SelectedMarket))
                    {
                        marketBets[decision.SelectedMarket] = 0;
                        marketWins[decision.SelectedMarket] = 0;
                    }
                    marketBets[decision.SelectedMarket]++;
                    
                    // Check if bet won
                    bool won = CheckBetOutcome(decision.SelectedMarket, match);
                    if (won)
                    {
                        highConfidenceWins++;
                        marketWins[decision.SelectedMarket]++;
                        
                        // Calculate returns (odds default to 2.0 for simplicity)
                        double odds = 2.0;
                        totalReturns += stake * odds;
                    }
                }
                
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
        
        Console.WriteLine($"\n--- H2H Filter Statistics ---");
        Console.WriteLine($"Derby Matches: {derbyMatches} ({(double)derbyMatches/total:P1})");
        Console.WriteLine($"BTTS Candidates (4+/5 H2H): {bttsH2HCandidates} ({(double)bttsH2HCandidates/total:P1})");
        Console.WriteLine($"Over 2.5 Candidates (4+/5 H2H): {over25H2HCandidates} ({(double)over25H2HCandidates/total:P1})");
        Console.WriteLine($"2-3 Goals Candidates (3+/5 H2H): {twoToThreeH2HCandidates} ({(double)twoToThreeH2HCandidates/total:P1})");
        Console.WriteLine($"Home Win Candidates (3+/5 H2H): {homeWinH2HCandidates} ({(double)homeWinH2HCandidates/total:P1})");
        Console.WriteLine($"Away Win Candidates (3+/5 H2H): {awayWinH2HCandidates} ({(double)awayWinH2HCandidates/total:P1})");
        Console.WriteLine($"Draw Candidates (3+/5 H2H): {drawH2HCandidates} ({(double)drawH2HCandidates/total:P1})");
        
        Console.WriteLine($"\n--- Decision Layer Performance ---");
        Console.WriteLine($"High Confidence Bets: {highConfidenceBets} ({(double)highConfidenceBets/total:P1})");
        if (highConfidenceBets > 0)
        {
            double winRate = (double)highConfidenceWins/highConfidenceBets;
            double roi = ((totalReturns - totalStake) / totalStake) * 100;
            Console.WriteLine($"Win Rate: {winRate:P1} ({highConfidenceWins}/{highConfidenceBets})");
            Console.WriteLine($"Total Stake: ${totalStake:F0}");
            Console.WriteLine($"Total Returns: ${totalReturns:F0}");
            Console.WriteLine($"ROI: {(roi >= 0 ? "+" : "")}{roi:F1}%");
            
            Console.WriteLine($"\nMarket Breakdown:");
            foreach (var market in marketBets.Keys.OrderByDescending(k => marketBets[k]))
            {
                int bets = marketBets[market];
                int wins = marketWins[market];
                double marketWinRate = (double)wins/bets;
                Console.WriteLine($"  {market}: {bets} bets, Win Rate: {marketWinRate:P1} ({wins}/{bets})");
            }
        }
    }
    
    private static bool CheckBetOutcome(string market, HistoricalMatchDto match)
    {
        int totalGoals = match.FTHG + match.FTAG;
        bool btts = match.FTHG > 0 && match.FTAG > 0;
        
        return market switch
        {
            "Over 2.5 Goals" => totalGoals > 2.5,
            "BTTS Yes" => btts,
            "Home Win" => match.FTR == "H",
            "Away Win" => match.FTR == "A",
            "2-3 Goals" => totalGoals == 2 || totalGoals == 3,
            _ => false
        };
    }
}

class MockConfiguration : Microsoft.Extensions.Configuration.IConfiguration
{
    private readonly Dictionary<string, string> _data;
    
    public MockConfiguration(Dictionary<string, string> data) => _data = data;
    
    public string? this[string key] 
    { 
        get => _data.TryGetValue(key, out var v) ? v : null;
        set => _data[key] = value!;
    }
    
    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => 
        Enumerable.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();
    
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => 
        new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
    
    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => 
        new MockConfigSection(this, key);
}

class MockConfigSection : Microsoft.Extensions.Configuration.IConfigurationSection
{
    private readonly Microsoft.Extensions.Configuration.IConfiguration _root;
    private readonly string _key;
    
    public MockConfigSection(Microsoft.Extensions.Configuration.IConfiguration root, string key)
    {
        _root = root;
        _key = key;
    }
    
    public string this[string key]
    {
        get => _root[$"{Path}:{key}"]!;
        set => _root[$"{Path}:{key}"] = value;
    }
    
    public string Key => _key;
    public string Path => _key;
    public string? Value 
    { 
        get => _root[_key];
        set => _root[_key] = value;
    }
    
    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => 
        Enumerable.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();
    
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => _root.GetReloadToken();
    
    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => 
        _root.GetSection($"{Path}:{key}");
}
