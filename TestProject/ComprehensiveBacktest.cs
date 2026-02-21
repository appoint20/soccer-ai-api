using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_infrastructure;

namespace TestProject;

class ComprehensiveBacktest
{
    public static async Task Run()
    {
        Console.WriteLine("=== Comprehensive Backtesting with Detailed Predictions (15 Weeks) ===\n");
        
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
        var mlPredictionService = provider.GetRequiredService<IMlPredictionService>();
        var trapDetectionService = provider.GetRequiredService<ITrapDetectionService>();
        
        // Load data
        Console.WriteLine("Loading historical data...");
        var allMatches = await excelService.GetAllMatchesAsync();
        
        if (allMatches.Count == 0)
        {
            Console.WriteLine("No historical data found.");
            return;
        }
        
        Console.WriteLine($"Loaded {allMatches.Count} matches.\n");
        
        // Define test period (last 15 weeks)
        var lastMatchDate = allMatches.Max(m => m.Date);
        var startDate = lastMatchDate.AddDays(-105); // ~15 weeks
        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        Console.WriteLine($"Test period: {startDate:yyyy-MM-dd} to {lastMatchDate:yyyy-MM-dd}");
        Console.WriteLine($"Total matches in test period: {testSet.Count}\n");
        
        // Track all predictions
        var predictions = new List<DetailedPrediction>();
        int processed = 0;
        int skipped = 0;
        
        Console.WriteLine("Processing predictions...");
        
        foreach (var match in testSet)
        {
            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
            if (pastHistory.Count < 500) 
            {
                skipped++;
                continue;
            }
            
            try
            {
                // Calculate ML prediction first
                var upcomingDtoForMl = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"),
                    Time = match.Date.ToString("HH:mm"),
                    Odds = match.Odds != null ? new MatchOdds 
                    { 
                        HomeWin = match.Odds.HomeWin, 
                        Draw = match.Odds.Draw, 
                        AwayWin = match.Odds.AwayWin,
                        Over25 = match.Odds.Over25,
                        Under25 = match.Odds.Under25,
                        BttsYes = match.Odds.BttsYes
                    } : null
                };
                
                var mlPrediction = await mlPredictionService.PredictMatchAsync(upcomingDtoForMl, pastHistory);
                
                // Calculate all analytics with league and ML prediction for improved decision making
                var analytics = await advancedStatsService.CalculateAnalyticsAsync(
                    match.HomeTeam, match.AwayTeam, pastHistory, match.League, mlPrediction);
                
                // Create upcoming DTO for trap detection
                var upcomingDto = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"),
                    Time = match.Date.ToString("HH:mm"),
                    League = match.League,
                    LeagueName = match.League,
                    Odds = match.Odds != null ? new MatchOdds 
                    { 
                        HomeWin = match.Odds.HomeWin, 
                        Draw = match.Odds.Draw, 
                        AwayWin = match.Odds.AwayWin,
                        Over25 = match.Odds.Over25,
                        Under25 = match.Odds.Under25,
                        BttsYes = match.Odds.BttsYes
                    } : null
                };
                
                // Trap Detection
                var traps = trapDetectionService.AnalyzeTraps(upcomingDto, analytics);
                
                var probs = analytics.Probabilities;
                var decision = analytics.Decision;
                var h2h = analytics.H2HAnalysis;
                
                // Create detailed prediction record
                var prediction = new DetailedPrediction
                {
                    // Match Info
                    Date = match.Date,
                    League = match.League ?? "Unknown",
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    
                    // Poisson/Dixon-Coles Probabilities
                    PoissonOver25 = probs.Over25,
                    PoissonBTTS = probs.BTTS,
                    PoissonHomeWin = probs.HomeWin,
                    PoissonDraw = probs.Draw,
                    PoissonAwayWin = probs.AwayWin,
                    Poisson2to3Goals = probs.Prob2to3Goals,
                    ExpectedHomeGoals = probs.ExpectedGoalsHome,
                    ExpectedAwayGoals = probs.ExpectedGoalsAway,
                    
                    // ML Model Probabilities
                    MlOver25 = mlPrediction?.Over25Probability ?? 0,
                    MlBTTS = mlPrediction?.BTTSProbability ?? 0,
                    MlHomeWin = mlPrediction?.HomeWinProbability ?? 0,
                    MlAwayWin = mlPrediction?.AwayWinProbability ?? 0,
                    MlDraw = mlPrediction?.DrawProbability ?? 0,
                    MlExpectedGoals = mlPrediction?.ExpectedGoals ?? 0,
                    
                    // H2H Analysis
                    IsDerby = h2h.IsDerby,
                    H2HBttsCandidate = h2h.IsBTTSCandidate,
                    H2HOver25Candidate = h2h.IsOver25Candidate,
                    H2H2to3Candidate = h2h.Is2to3GoalsCandidate,
                    H2HHomeWinCandidate = h2h.IsHomeWinCandidate,
                    H2HAwayWinCandidate = h2h.IsAwayWinCandidate,
                    H2HDrawCandidate = h2h.IsDrawCandidate,
                    
                    // Trap Warnings
                    HasTraps = traps.Any(),
                    TrapWarnings = string.Join("; ", traps),
                    
                    // Recommended Bet (from Decision Layer)
                    RecommendedBet = decision.IsHighConfidence ? decision.SelectedMarket : "None",
                    Confidence = decision.IsHighConfidence ? decision.Confidence : 0,
                    ExpectedValue = decision.IsHighConfidence ? decision.ExpectedValue : 0,
                    Reasons = decision.IsHighConfidence ? string.Join(" | ", decision.Reasons) : "",
                    
                    // Odds
                    OddsHomeWin = match.Odds?.HomeWin ?? 0,
                    OddsDraw = match.Odds?.Draw ?? 0,
                    OddsAwayWin = match.Odds?.AwayWin ?? 0,
                    OddsOver25 = match.Odds?.Over25 ?? 0,
                    OddsUnder25 = match.Odds?.Under25 ?? 0,
                    OddsBttsYes = match.Odds?.BttsYes ?? 0,
                    
                    // Actual Results
                    ActualScore = $"{match.FTHG}-{match.FTAG}",
                    ActualResult = match.FTR,
                    ActualTotalGoals = match.FTHG + match.FTAG,
                    ActualOver25 = (match.FTHG + match.FTAG) > 2.5,
                    ActualBTTS = (match.FTHG > 0 && match.FTAG > 0),
                    Actual2to3Goals = (match.FTHG + match.FTAG == 2 || match.FTHG + match.FTAG == 3)
                };
                
                // Calculate bet outcomes if recommended
                if (decision.IsHighConfidence)
                {
                    CalculateBetOutcome(prediction, match);
                }
                
                predictions.Add(prediction);
                
                processed++;
                if (processed % 50 == 0)
                {
                    Console.Write($"\rProcessed: {processed}/{testSet.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing {match.HomeTeam} vs {match.AwayTeam}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"\n\nProcessing complete!");
        Console.WriteLine($"Total processed: {processed}");
        Console.WriteLine($"Skipped (insufficient history): {skipped}");
        Console.WriteLine($"Total predictions: {predictions.Count}\n");
        
        // Generate outputs
        var outputDir = "/Users/shivm/.gemini/antigravity/brain/6cf8bff2-a8db-4706-9213-fed8cadb7232";
        Directory.CreateDirectory(outputDir);
        
        var csvPath = Path.Combine(outputDir, "comprehensive_backtest.csv");
        var reportPath = Path.Combine(outputDir, "backtest_analysis.md");
        
        WriteDetailedCSV(predictions, csvPath);
        var report = GenerateAnalysisReport(predictions);
        File.WriteAllText(reportPath, report);
        
        Console.WriteLine($"CSV saved to: {csvPath}");
        Console.WriteLine($"Analysis report saved to: {reportPath}");
        
        // Print summary to console
        Console.WriteLine("\n" + GenerateSummary(predictions));
    }
    
    private static void CalculateBetOutcome(DetailedPrediction pred, HistoricalMatchDto match)
    {
        int totalGoals = match.FTHG + match.FTAG;
        bool btts = match.FTHG > 0 && match.FTAG > 0;
        
        bool won = pred.RecommendedBet switch
        {
            "Over 2.5 Goals" => totalGoals > 2.5,
            "BTTS Yes" => btts,
            "Home Win" => match.FTR == "H",
            "Away Win" => match.FTR == "A",
            "2-3 Goals" => totalGoals == 2 || totalGoals == 3,
            "Draw" => match.FTR == "D",
            _ => false
        };
        
        pred.BetOutcome = won ? "Won" : "Lost";
        
        // Get appropriate odds
        decimal odds = pred.RecommendedBet switch
        {
            "Over 2.5 Goals" => pred.OddsOver25,
            "BTTS Yes" => pred.OddsBttsYes,
            "Home Win" => pred.OddsHomeWin,
            "Away Win" => pred.OddsAwayWin,
            "Draw" => pred.OddsDraw,
            _ => 2.0m
        };
        
        if (odds == 0) odds = 2.0m; // Default if missing
        
        pred.BetOdds = odds;
        pred.Stake = 10.0;
        pred.Return = won ? pred.Stake * (double)odds : 0;
        pred.Profit = pred.Return - pred.Stake;
    }
    
    private static void WriteDetailedCSV(List<DetailedPrediction> predictions, string path)
    {
        var csv = new StringBuilder();
        
        // Header
        csv.AppendLine("Date,League,Home Team,Away Team," +
                      "Poisson Over2.5,Poisson BTTS,Poisson Home,Poisson Draw,Poisson Away,Poisson 2-3,Exp Home Goals,Exp Away Goals," +
                      "ML Over2.5,ML BTTS,ML Home,ML Draw,ML Away,ML Exp Goals," +
                      "Derby,H2H BTTS,H2H Over2.5,H2H 2-3,H2H Home,H2H Away,H2H Draw," +
                      "Has Traps,Trap Warnings," +
                      "Recommended Bet,Confidence,Expected Value,Reasons," +
                      "Odds Home,Odds Draw,Odds Away,Odds Over2.5,Odds Under2.5,Odds BTTS," +
                      "Actual Score,Actual Result,Actual Goals,Actual Over2.5,Actual BTTS,Actual 2-3," +
                      "Bet Outcome,Bet Odds,Stake,Return,Profit");
        
        // Data rows
        foreach (var p in predictions.OrderBy(x => x.Date))
        {
            csv.AppendLine($"{p.Date:yyyy-MM-dd},{Esc(p.League)},{Esc(p.HomeTeam)},{Esc(p.AwayTeam)}," +
                          $"{p.PoissonOver25:F4},{p.PoissonBTTS:F4},{p.PoissonHomeWin:F4},{p.PoissonDraw:F4},{p.PoissonAwayWin:F4},{p.Poisson2to3Goals:F4},{p.ExpectedHomeGoals:F2},{p.ExpectedAwayGoals:F2}," +
                          $"{p.MlOver25:F4},{p.MlBTTS:F4},{p.MlHomeWin:F4},{p.MlDraw:F4},{p.MlAwayWin:F4},{p.MlExpectedGoals:F2}," +
                          $"{p.IsDerby},{p.H2HBttsCandidate},{p.H2HOver25Candidate},{p.H2H2to3Candidate},{p.H2HHomeWinCandidate},{p.H2HAwayWinCandidate},{p.H2HDrawCandidate}," +
                          $"{p.HasTraps},\"{p.TrapWarnings.Replace("\"", "\"\"")}\"," +
                          $"{Esc(p.RecommendedBet)},{p.Confidence:F4},{p.ExpectedValue:F4},\"{p.Reasons.Replace("\"", "\"\"")}\"," +
                          $"{p.OddsHomeWin:F2},{p.OddsDraw:F2},{p.OddsAwayWin:F2},{p.OddsOver25:F2},{p.OddsUnder25:F2},{p.OddsBttsYes:F2}," +
                          $"{Esc(p.ActualScore)},{p.ActualResult},{p.ActualTotalGoals},{p.ActualOver25},{p.ActualBTTS},{p.Actual2to3Goals}," +
                          $"{p.BetOutcome},{p.BetOdds:F2},{p.Stake:F2},{p.Return:F2},{p.Profit:F2}");
        }
        
        File.WriteAllText(path, csv.ToString());
    }
    
    private static string Esc(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
    
    private static string GenerateSummary(List<DetailedPrediction> predictions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== QUICK SUMMARY ===");
        sb.AppendLine($"Total Predictions: {predictions.Count}");
        
        var bets = predictions.Where(p => p.RecommendedBet != "None").ToList();
        if (bets.Any())
        {
            var wins = bets.Count(p => p.BetOutcome == "Won");
            var totalStake = bets.Sum(p => p.Stake);
            var totalReturn = bets.Sum(p => p.Return);
            var roi = ((totalReturn - totalStake) / totalStake) * 100;
            
            sb.AppendLine($"\nHigh Confidence Bets: {bets.Count}");
            sb.AppendLine($"Win Rate: {(double)wins/bets.Count:P1} ({wins}/{bets.Count})");
            sb.AppendLine($"Total Stake: ${totalStake:F0}");
            sb.AppendLine($"Total Return: ${totalReturn:F0}");
            sb.AppendLine($"Net Profit: ${totalReturn - totalStake:F0}");
            sb.AppendLine($"ROI: {roi:+0.0;-0.0}%");
            
            sb.AppendLine($"\nBy Market:");
            foreach (var market in bets.GroupBy(p => p.RecommendedBet))
            {
                var mWins = market.Count(p => p.BetOutcome == "Won");
                var mTotal = market.Count();
                var mStake = market.Sum(p => p.Stake);
                var mReturn = market.Sum(p => p.Return);
                var mRoi = ((mReturn - mStake) / mStake) * 100;
                
                sb.AppendLine($"  {market.Key}: {mWins}/{mTotal} ({(double)mWins/mTotal:P0}) | ROI: {mRoi:+0.0;-0.0}%");
            }
        }
        
        return sb.ToString();
    }
    
    private static string GenerateAnalysisReport(List<DetailedPrediction> predictions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Comprehensive Backtest Analysis Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        
        sb.AppendLine("## Overview");
        sb.AppendLine($"- Total Matches Analyzed: {predictions.Count}");
        
        var bets = predictions.Where(p => p.RecommendedBet != "None").ToList();
        sb.AppendLine($"- High Confidence Bets Generated: {bets.Count} ({(double)bets.Count/predictions.Count:P1})");
        sb.AppendLine();
        
        // Performance Summary
        if (bets.Any())
        {
            sb.AppendLine("## Performance Summary");
            sb.AppendLine();
            
            var wins = bets.Count(p => p.BetOutcome == "Won");
            var totalStake = bets.Sum(p => p.Stake);
            var totalReturn = bets.Sum(p => p.Return);
            var roi = ((totalReturn - totalStake) / totalStake) * 100;
            var avgOdds = bets.Average(p => (double)p.BetOdds);
            
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine($"| Total Bets | {bets.Count} |");
            sb.AppendLine($"| Wins | {wins} |");
            sb.AppendLine($"| Losses | {bets.Count - wins} |");
            sb.AppendLine($"| Win Rate | {(double)wins/bets.Count:P1} |");
            sb.AppendLine($"| Average Odds | {avgOdds:F2} |");
            sb.AppendLine($"| Total Stake | ${totalStake:F0} |");
            sb.AppendLine($"| Total Return | ${totalReturn:F0} |");
            sb.AppendLine($"| Net Profit | ${totalReturn - totalStake:F0} |");
            sb.AppendLine($"| ROI | {roi:+0.0;-0.0}% |");
            sb.AppendLine();
            
            // Market Breakdown
            sb.AppendLine("## Market Performance");
            sb.AppendLine();
            sb.AppendLine("| Market | Bets | Wins | Win Rate | Avg Odds | ROI |");
            sb.AppendLine("|--------|------|------|----------|----------|-----|");
            
            foreach (var market in bets.GroupBy(p => p.RecommendedBet).OrderByDescending(g => g.Count()))
            {
                var mWins = market.Count(p => p.BetOutcome == "Won");
                var mTotal = market.Count();
                var mStake = market.Sum(p => p.Stake);
                var mReturn = market.Sum(p => p.Return);
                var mRoi = ((mReturn - mStake) / mStake) * 100;
                var mAvgOdds = market.Average(p => (double)p.BetOdds);
                
                sb.AppendLine($"| {market.Key} | {mTotal} | {mWins} | {(double)mWins/mTotal:P0} | {mAvgOdds:F2} | {mRoi:+0.0;-0.0}% |");
            }
            sb.AppendLine();
            
            // League Performance
            sb.AppendLine("## League Performance");
            sb.AppendLine();
            sb.AppendLine("| League | Bets | Win Rate | ROI |");
            sb.AppendLine("|--------|------|----------|-----|");
            
            foreach (var league in bets.GroupBy(p => p.League).OrderByDescending(g => g.Count()))
            {
                var lWins = league.Count(p => p.BetOutcome == "Won");
                var lTotal = league.Count();
                var lStake = league.Sum(p => p.Stake);
                var lReturn = league.Sum(p => p.Return);
                var lRoi = ((lReturn - lStake) / lStake) * 100;
                
                sb.AppendLine($"| {league.Key} | {lTotal} | {(double)lWins/lTotal:P0} | {lRoi:+0.0;-0.0}% |");
            }
            sb.AppendLine();
        }
        
        // Model Agreement Analysis
        sb.AppendLine("## Model Agreement Analysis");
        sb.AppendLine();
        var highAgreement = predictions.Where(p => 
            Math.Abs(p.PoissonOver25 - p.MlOver25) < 0.15 && 
            p.PoissonOver25 > 0.6).ToList();
        sb.AppendLine($"- Matches with high model agreement (both >60% on Over 2.5): {highAgreement.Count}");
        if (highAgreement.Any())
        {
            var haWins = highAgreement.Count(p => p.ActualOver25);
            sb.AppendLine($"  - Actual Over 2.5 rate: {(double)haWins/highAgreement.Count:P1}");
        }
        sb.AppendLine();
        
        // Trap Detection Effectiveness
        sb.AppendLine("## Trap Detection Effectiveness");
        sb.AppendLine();
        var withTraps = predictions.Where(p => p.HasTraps).ToList();
        sb.AppendLine($"- Matches with trap warnings: {withTraps.Count}");
        if (withTraps.Any())
        {
            var trappedBets = withTraps.Where(p => p.RecommendedBet != "None").ToList();
            sb.AppendLine($"- High confidence bets despite traps: {trappedBets.Count}");
            if (trappedBets.Any())
            {
                var trappedWins = trappedBets.Count(p => p.BetOutcome == "Won");
                sb.AppendLine($"  - Win rate: {(double)trappedWins/trappedBets.Count:P1}");
            }
        }
        sb.AppendLine();
        
        // H2H Filter Impact
        sb.AppendLine("## H2H Filter Impact");
        sb.AppendLine();
        var h2hBets = bets.Where(p => p.H2HBttsCandidate || p.H2HOver25Candidate || p.H2H2to3Candidate).ToList();
        sb.AppendLine($"- Bets with H2H support: {h2hBets.Count} ({(double)h2hBets.Count/Math.Max(bets.Count, 1):P1})");
        if (h2hBets.Any())
        {
            var h2hWins = h2hBets.Count(p => p.BetOutcome == "Won");
            sb.AppendLine($"  - Win rate with H2H support: {(double)h2hWins/h2hBets.Count:P1}");
        }
        var noH2hBets = bets.Except(h2hBets).ToList();
        if (noH2hBets.Any())
        {
            var noH2hWins = noH2hBets.Count(p => p.BetOutcome == "Won");
            sb.AppendLine($"- Bets without H2H support: {noH2hBets.Count}");
            sb.AppendLine($"  - Win rate without H2H support: {(double)noH2hWins/noH2hBets.Count:P1}");
        }
        sb.AppendLine();
        
        // Improvement Recommendations
        sb.AppendLine("## Improvement Recommendations");
        sb.AppendLine();
        
        // Check various patterns
        var recommendations = new List<string>();
        
        if (bets.Any())
        {
            var avgConfidence = bets.Average(p => p.Confidence);
            var highConfBets = bets.Where(p => p.Confidence > 0.75).ToList();
            var medConfBets = bets.Where(p => p.Confidence >= 0.60 && p.Confidence <= 0.75).ToList();
            
            if (highConfBets.Any())
            {
                var highConfWinRate = (double)highConfBets.Count(p => p.BetOutcome == "Won")/highConfBets.Count;
                recommendations.Add($"**Confidence Threshold**: High confidence bets (>75%) have {highConfWinRate:P0} win rate. Consider filtering to only bet at >75% confidence.");
            }
            
            if (h2hBets.Any() && noH2hBets.Any())
            {
                var h2hWr = (double)h2hBets.Count(p => p.BetOutcome == "Won")/h2hBets.Count;
                var noH2hWr = (double)noH2hBets.Count(p => p.BetOutcome == "Won")/noH2hBets.Count;
                if (h2hWr > noH2hWr + 0.05)
                {
                    recommendations.Add($"**H2H Filter**: Bets with H2H support perform {((h2hWr - noH2hWr)*100):F0}% better. Require H2H support for all bets.");
                }
            }
            
            var lowOddsBets = bets.Where(p => p.BetOdds < 1.8m).ToList();
            if (lowOddsBets.Any())
            {
                var lowOddsWr = (double)lowOddsBets.Count(p => p.BetOutcome == "Won")/lowOddsBets.Count;
                recommendations.Add($"**Odds Filter**: Bets with odds <1.8 have {lowOddsWr:P0} win rate. Consider minimum odds threshold.");
            }
            
            // Check worst performing markets
            var worstMarket = bets.GroupBy(p => p.RecommendedBet)
                .Select(g => new { Market = g.Key, WR = (double)g.Count(p => p.BetOutcome == "Won")/g.Count() })
                .OrderBy(x => x.WR)
                .FirstOrDefault();
            if (worstMarket != null && worstMarket.WR < 0.5)
            {
                recommendations.Add($"**Avoid {worstMarket.Market}**: This market has only {worstMarket.WR:P0} win rate. Consider removing or adjusting confidence threshold.");
            }
        }
        
        if (recommendations.Any())
        {
            foreach (var rec in recommendations)
            {
                sb.AppendLine($"- {rec}");
            }
        }
        else
        {
            sb.AppendLine("- Continue monitoring performance with more data");
        }
        
        return sb.ToString();
    }
}

class DetailedPrediction
{
    // Match Info
    public DateTime Date { get; set; }
    public string League { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    
    // Poisson/Dixon-Coles
    public double PoissonOver25 { get; set; }
    public double PoissonBTTS { get; set; }
    public double PoissonHomeWin { get; set; }
    public double PoissonDraw { get; set; }
    public double PoissonAwayWin { get; set; }
    public double Poisson2to3Goals { get; set; }
    public double ExpectedHomeGoals { get; set; }
    public double ExpectedAwayGoals { get; set; }
    
    // ML Model
    public double MlOver25 { get; set; }
    public double MlBTTS { get; set; }
    public double MlHomeWin { get; set; }
    public double MlDraw { get; set; }
    public double MlAwayWin { get; set; }
    public double MlExpectedGoals { get; set; }
    
    // H2H
    public bool IsDerby { get; set; }
    public bool H2HBttsCandidate { get; set; }
    public bool H2HOver25Candidate { get; set; }
    public bool H2H2to3Candidate { get; set; }
    public bool H2HHomeWinCandidate { get; set; }
    public bool H2HAwayWinCandidate { get; set; }
    public bool H2HDrawCandidate { get; set; }
    
    // Traps
    public bool HasTraps { get; set; }
    public string TrapWarnings { get; set; } = "";
    
    // Recommendation
    public string RecommendedBet { get; set; } = "None";
    public double Confidence { get; set; }
    public double ExpectedValue { get; set; }
    public string Reasons { get; set; } = "";
    
    // Odds
    public decimal OddsHomeWin { get; set; }
    public decimal OddsDraw { get; set; }
    public decimal OddsAwayWin { get; set; }
    public decimal OddsOver25 { get; set; }
    public decimal OddsUnder25 { get; set; }
    public decimal OddsBttsYes { get; set; }
    
    // Actual Results
    public string ActualScore { get; set; } = "";
    public string ActualResult { get; set; } = "";
    public int ActualTotalGoals { get; set; }
    public bool ActualOver25 { get; set; }
    public bool ActualBTTS { get; set; }
    public bool Actual2to3Goals { get; set; }
    
    // Bet Outcome
    public string BetOutcome { get; set; } = "";
    public decimal BetOdds { get; set; }
    public double Stake { get; set; }
    public double Return { get; set; }
    public double Profit { get; set; }
}
