using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure;

namespace TestProject;

class DetailedBacktest
{
    public static async Task Run()
    {
        Console.WriteLine("=== 15-Week Detailed Backtest ===\n");
        
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
        
        var excelService = provider.GetRequiredService<IHistoricalDataRepository>();
        var advancedStatsService = provider.GetRequiredService<IAdvancedStatsService>();
        
        // Load data
        Console.WriteLine("Loading historical data...");
        var allMatches = await excelService.GetAllMatchesAsync();
        
        var lastMatchDate = allMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-105); // 15 weeks
        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        Console.WriteLine($"Test period: {startDate:yyyy-MM-dd} to {lastMatchDate:yyyy-MM-dd}");
        Console.WriteLine($"Total matches: {testSet.Count}\n");
        
        // Track all predictions
        var predictions = new List<PredictionRecord>();
        int processed = 0;
        
        foreach (var match in testSet)
        {
            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
            if (pastHistory.Count < 500) continue;
            
            try
            {
                var result = await advancedStatsService.CalculateAnalyticsAsync(
                    match.HomeTeam, match.AwayTeam, pastHistory);
                
                var decision = result.Decision;
                
                if (decision.IsHighConfidence)
                {
                    var record = new PredictionRecord
                    {
                        Date = match.Date,
                        League = match.League,
                        HomeTeam = match.HomeTeam,
                        AwayTeam = match.AwayTeam,
                        PredictedMarket = decision.SelectedMarket,
                        Confidence = decision.Confidence,
                        ExpectedValue = decision.ExpectedValue,
                        H2HSupport = decision.HasH2HSupport,
                        Reasons = string.Join(" | ", decision.Reasons),
                        
                        // Actual results
                        ActualScore = $"{match.FTHG}-{match.FTAG}",
                        ActualResult = match.FTR,
                        ActualBTTS = (match.FTHG > 0 && match.FTAG > 0) ? "Yes" : "No",
                        ActualOver25 = (match.FTHG + match.FTAG > 2.5) ? "Yes" : "No",
                        
                        // Bet outcome
                        BetWon = CheckBetWon(decision.SelectedMarket, match),
                        Odds = 2.0, // Default odds
                        Stake = 10.0,
                        Return = CheckBetWon(decision.SelectedMarket, match) ? 20.0 : 0.0,
                        Profit = CheckBetWon(decision.SelectedMarket, match) ? 10.0 : -10.0
                    };
                    
                    predictions.Add(record);
                }
                
                processed++;
                if (processed % 100 == 0)
                {
                    Console.Write($"\rProcessed: {processed}/{testSet.Count}");
                }
            }
            catch (Exception ex)
            {
                // Skip errors
            }
        }
        
        Console.WriteLine($"\n\nGenerated {predictions.Count} high-confidence predictions");
        
        // Write to CSV
        var csvPath = "/Users/shivm/.gemini/antigravity/brain/e5c4006c-5d70-437d-8bd7-d89707fda0ad/predictions_15weeks.csv";
        WriteCSV(predictions, csvPath);
        
        Console.WriteLine($"CSV saved to: {csvPath}");
        
        // Summary stats
        var wins = predictions.Count(p => p.BetWon);
        var totalStake = predictions.Sum(p => p.Stake);
        var totalReturn = predictions.Sum(p => p.Return);
        var roi = ((totalReturn - totalStake) / totalStake) * 100;
        
        Console.WriteLine($"\n=== SUMMARY ===");
        Console.WriteLine($"Total Predictions: {predictions.Count}");
        Console.WriteLine($"Wins: {wins} ({(double)wins/predictions.Count:P1})");
        Console.WriteLine($"Total Stake: ${totalStake:F0}");
        Console.WriteLine($"Total Return: ${totalReturn:F0}");
        Console.WriteLine($"Profit: ${totalReturn - totalStake:F0}");
        Console.WriteLine($"ROI: {roi:+0.0;-0.0}%");
        
        // Market breakdown
        Console.WriteLine($"\n=== MARKET BREAKDOWN ===");
        foreach (var market in predictions.GroupBy(p => p.PredictedMarket))
        {
            var marketWins = market.Count(p => p.BetWon);
            var marketTotal = market.Count();
            Console.WriteLine($"{market.Key,-20} {marketTotal,4} bets | {(double)marketWins/marketTotal:P1} win rate");
        }
    }
    
    private static bool CheckBetWon(string market, HistoricalMatchDto match)
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
    
    private static void WriteCSV(List<PredictionRecord> predictions, string path)
    {
        var csv = new StringBuilder();
        
        // Header
        csv.AppendLine("Date,League,Home Team,Away Team,Predicted Market,Confidence,Expected Value,H2H Support,Odds,Stake,Actual Score,Actual Result,Actual BTTS,Actual Over 2.5,Bet Won,Return,Profit,Reasons");
        
        // Rows
        foreach (var p in predictions.OrderBy(x => x.Date))
        {
            csv.AppendLine($"{p.Date:yyyy-MM-dd}," +
                          $"{EscapeCsv(p.League)}," +
                          $"{EscapeCsv(p.HomeTeam)}," +
                          $"{EscapeCsv(p.AwayTeam)}," +
                          $"{EscapeCsv(p.PredictedMarket)}," +
                          $"{p.Confidence:F3}," +
                          $"{p.ExpectedValue:F3}," +
                          $"{p.H2HSupport}," +
                          $"{p.Odds:F2}," +
                          $"{p.Stake:F2}," +
                          $"{EscapeCsv(p.ActualScore)}," +
                          $"{p.ActualResult}," +
                          $"{p.ActualBTTS}," +
                          $"{p.ActualOver25}," +
                          $"{p.BetWon}," +
                          $"{p.Return:F2}," +
                          $"{p.Profit:F2}," +
                          $"\"{p.Reasons.Replace("\"", "\"\"")}\"");
        }
        
        File.WriteAllText(path, csv.ToString());
    }
    
    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

class PredictionRecord
{
    public DateTime Date { get; set; }
    public string League { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public string PredictedMarket { get; set; } = "";
    public double Confidence { get; set; }
    public double ExpectedValue { get; set; }
    public bool H2HSupport { get; set; }
    public string Reasons { get; set; } = "";
    
    public string ActualScore { get; set; } = "";
    public string ActualResult { get; set; } = "";
    public string ActualBTTS { get; set; } = "";
    public string ActualOver25 { get; set; } = "";
    
    public bool BetWon { get; set; }
    public double Odds { get; set; }
    public double Stake { get; set; }
    public double Return { get; set; }
    public double Profit { get; set; }
}
