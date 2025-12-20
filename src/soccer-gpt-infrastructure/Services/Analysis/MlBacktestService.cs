using System.Text;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.Analysis;

public class MlBacktestService(
    IHistoricalDataRepository historicalRepository,
    IMlPredictionService mlService,
    ILogger<MlBacktestService> logger)
{
    public async Task<string> RunBacktestAsync(int weeks = 10)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Backtest from {Start} to {End} (Max Date found)", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        if (testSet.Count == 0) return $"No matches found in the last {weeks} weeks (since {startDate:d}). Latest match is {lastDate:d}.";

        var sb = new StringBuilder();
        sb.AppendLine($"# ML Model Backtest Report (Last {weeks} Weeks)");
        sb.AppendLine($"Date Range: {startDate:d} - {lastDate:d}");
        sb.AppendLine($"Total Matches Tested: {testSet.Count}");
        sb.AppendLine("");
        sb.AppendLine("| Strategy | Bets | Wins | Losses | Strike Rate | Avg Odds | Profit | ROI |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        // Metrics
        int betsOver25 = 0;
        int winsOver25 = 0;
        double stakeOver25 = 0;
        double profitOver25 = 0;

        int betsBtts = 0;
        int winsBtts = 0;
        double stakeBtts = 0;
        double profitBtts = 0;

        // Confidence Thresholds
        double confThreshold25 = 0.60;
        double confThresholdBtts = 0.60;

        // We must process sequentially to simulate "past knowing future"
        // But for performance, we can just pass the FULL history to CalculateFeatures, 
        // PROVIDED that FeatureEngineering filtering logic respects DATE.
        // Checking FeatureEngineeringService... line 28: "Take(i)" where i is index in Sorted List.
        // It strictly takes PAST matches.
        // However, `MlPredictionService` currently takes `allHistory` and creates `target`.
        // `FeatureEngineeringService` takes `(target, history)`. 
        // It filters `history` to be *before* `target` anyway?
        // Let's verify `FeatureEngineeringService`.
        // Line 64: `history.Where(m => m.Date < targetMatch.Date)` - Wait, I recall seeing `Take(i)` in `CreateTrainingDataset`.
        // But `CalculateFeatures` takes a list. Does it filter by date manually?
        // Checking `FeatureEngineeringService`...
        // Line 64: `var homeHistory = history.Where(...).OrderByDescending(m => m.Date).ToList();`
        // It does NOT explicitly filter `m.Date < target.Date` inside `CalculateFeatures` if `history` contains future matches.
        // So I MUST pass only PAST history to `CalculateFeatures`.
        // This makes the loop slower but correct (O(N^2)).
        // `allMatches` is ~28k. 
        // Filtering `allMatches.Where(m => m.Date < current.Date)` for each test match is acceptable for backtest (~500 matches).

        int processed = 0;
        double totalOddsSum = 0;
        int betsWithOdds = 0;

        foreach (var match in testSet)
        {
            processed++;
            if (processed % 50 == 0) logger.LogInformation("Backtesting match {N}/{Total}", processed, testSet.Count);

            // Create pseudo-upcoming
            var upcomingDto = new UpcomingMatchDto
            {
                HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam,
                Date = match.Date.ToString("yyyy-MM-dd"),
                Time = match.Date.ToString("HH:mm"),
                Odds = match.Odds != null ? new MatchOdds 
                { 
                    Over25 = match.Odds.Over25, 
                    HomeWin = match.Odds.HomeWin,
                    AwayWin = match.Odds.AwayWin
                } : null
            };

            // STRICTLY Past History
            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();

            var prediction = await mlService.PredictMatchAsync(upcomingDto, pastHistory);

            if (prediction == null) continue;

            // Analyze Over 2.5
            if (prediction.Over25Probability > confThreshold25)
            {
                betsOver25++;
                stakeOver25 += 1.0;
                
                decimal odds = match.Odds?.Over25 ?? 0;
                if (odds > 0) 
                {
                    totalOddsSum += (double)odds;
                    betsWithOdds++;
                }
                else
                {
                    // Use conservative default if missing, for estimation
                    odds = 1.60m; 
                }

                bool isOver = (match.FTHG + match.FTAG) > 2.5;
                if (isOver)
                {
                    winsOver25++;
                    profitOver25 += (double)odds - 1.0;
                }
                else
                {
                    profitOver25 -= 1.0;
                }
            }
        }

        double roi25 = stakeOver25 > 0 ? (profitOver25 / stakeOver25) * 100 : 0;
        double winRate25 = betsOver25 > 0 ? ((double)winsOver25 / betsOver25) * 100 : 0;
        double avgOdds = betsWithOdds > 0 ? totalOddsSum / betsWithOdds : 0;
        int losses25 = betsOver25 - winsOver25;

        sb.AppendLine($"| Over 2.5 (>{confThreshold25*100:0}%) | {betsOver25} | {winsOver25} | {losses25} | {winRate25:F1}% | {avgOdds:F2} | {profitOver25:F2}u | {roi25:F2}% |");
        sb.AppendLine("");
        sb.AppendLine("## Analysis");
        sb.AppendLine($"- **Bets Placed**: {betsOver25}");
        sb.AppendLine($"- **Correct Predictions**: {winsOver25}");
        sb.AppendLine($"- **Wrong Predictions**: {losses25}");
        sb.AppendLine($"- **Average Odds**: {avgOdds:F2} (Based on {betsWithOdds} matches with odds)");
        sb.AppendLine($"- **Net Profit**: {profitOver25:F2} units");
        sb.AppendLine($"- **ROI Calculation**: (Net Profit {profitOver25:F2} / Total Stakes {stakeOver25}) * 100 = {roi25:F2}%");
        
        return sb.ToString();
    }

    public async Task<string> RunAccumulatorBacktestAsync(int weeks = 10, double minOdds = 1.77, int ticketSize = 3)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Accumulator Backtest from {Start} to {End}", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        var eligibleBets = new List<(UpcomingMatchDto Match, decimal Odds, bool Won)>();
        double confThreshold = 0.60;

        int processed = 0;
        foreach (var match in testSet)
        {
            processed++;
            // Create pseudo-upcoming
            var upcomingDto = new UpcomingMatchDto
            {
                HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam,
                Date = match.Date.ToString("yyyy-MM-dd"),
                Time = match.Date.ToString("HH:mm"),
                Odds = match.Odds != null ? new MatchOdds 
                { 
                    Over25 = match.Odds.Over25, 
                    HomeWin = match.Odds.HomeWin,
                    AwayWin = match.Odds.AwayWin
                } : null
            };

            // Filter by Odds Pre-Check (optimization)
            if ((upcomingDto.Odds?.Over25 ?? 0) < (decimal)minOdds) continue;

            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
            var prediction = await mlService.PredictMatchAsync(upcomingDto, pastHistory);

            if (prediction != null && prediction.Over25Probability > confThreshold)
            {
                bool won = (match.FTHG + match.FTAG) > 2.5;
                eligibleBets.Add((upcomingDto, upcomingDto.Odds!.Over25, won));
            }
        }

        // Generate Tickets
        var tickets = eligibleBets.Chunk(ticketSize).ToList();
        
        // Analyze Tickets
        int totalTickets = tickets.Count;
        int wonTickets = 0;
        double totalProfit = 0;
        double totalStake = totalTickets; // 1 unit per ticket

        foreach (var ticket in tickets)
        {
            if (ticket.Length < ticketSize) continue; // Skip incomplete last ticket

            bool ticketWon = ticket.All(leg => leg.Won);
            double ticketOdds = ticket.Aggregate(1.0, (acc, leg) => acc * (double)leg.Odds);

            if (ticketWon)
            {
                wonTickets++;
                totalProfit += ticketOdds - 1.0;
            }
            else
            {
                totalProfit -= 1.0;
            }
        }

        double roi = totalStake > 0 ? (totalProfit / totalStake) * 100 : 0;
        
        var sb = new StringBuilder();
        sb.AppendLine($"# Accumulator Backtest (Last {weeks} Weeks)");
        sb.AppendLine($"Strategy: Over 2.5 | Min Odds: {minOdds} | Ticket Size: {ticketSize}");
        sb.AppendLine($"Eligible Legs Found: {eligibleBets.Count}");
        sb.AppendLine($"Total Tickets Generated: {totalTickets}");
        sb.AppendLine("");
        sb.AppendLine($"| Type | Tickets | Wins | Losses | ROI | Net Profit |");
        sb.AppendLine($"|---|---|---|---|---|---|");
        sb.AppendLine($"| {ticketSize}-Fold Acca | {totalTickets} | {wonTickets} | {totalTickets - wonTickets} | {roi:F2}% | {totalProfit:F2}u |");
        sb.AppendLine("");
        sb.AppendLine("## Ticket Details");

        int ticketIndex = 0;
        foreach (var ticket in tickets)
        {
            if (ticket.Length < ticketSize) continue;
            ticketIndex++;

            bool ticketWon = ticket.All(leg => leg.Won);
            double ticketOdds = ticket.Aggregate(1.0, (acc, leg) => acc * (double)leg.Odds);
            string resultStatus = ticketWon ? "**WON**" : "LOST";

            sb.AppendLine($"### Ticket #{ticketIndex} - Odds: {ticketOdds:F2} - {resultStatus}");
            sb.AppendLine("| Date | Match | Odds | Result |");
            sb.AppendLine("|---|---|---|---|");
            
            foreach (var leg in ticket)
            {
                string ledResult = leg.Won ? "WON" : "LOST";
                sb.AppendLine($"| {leg.Match.Date} | {leg.Match.HomeTeam} vs {leg.Match.AwayTeam} | {leg.Odds} | {ledResult} |");
            }
            sb.AppendLine("");
        }

        return sb.ToString();
    }

    public async Task<string> RunDailyPortfolioBacktestAsync(int weeks = 10, double minOdds = 1.77)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Daily Portfolio Backtest from {Start} to {End}", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        var groupedByDate = testSet.GroupBy(m => m.Date.Date).OrderBy(g => g.Key);

        var portfolioReport = new StringBuilder();
        portfolioReport.AppendLine($"# Daily Portfolio Backtest (Last {weeks} Weeks)");
        portfolioReport.AppendLine($"Strategy: 3 Tickets Per Day | Same Day Only | Min Odds {minOdds}");
        portfolioReport.AppendLine("Ticket 1: 1x HDW + 2x Goals | Ticket 2 & 3: 3x Goals (BTTS/Over)");
        portfolioReport.AppendLine("");

        int totalDaysFound = 0;
        double totalPortfolioProfit = 0;

        foreach (var dayGroup in groupedByDate)
        {
            var date = dayGroup.Key;
            var dayMatches = dayGroup.ToList();
            
            // Pools
            var hdwCandidates = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
            var goalsCandidates = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();

            foreach (var match in dayMatches)
            {
                var upcomingDto = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"),
                    Time = match.Date.ToString("HH:mm"),
                    Odds = match.Odds != null ? new MatchOdds 
                    { 
                        Over25 = match.Odds.Over25, 
                        HomeWin = match.Odds.HomeWin,
                        AwayWin = match.Odds.AwayWin,
                        BttsYes = match.Odds.BttsYes
                    } : null
                };

                // We need prediction
                var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
                var prediction = await mlService.PredictMatchAsync(upcomingDto, pastHistory);
                if (prediction == null) continue;

                // Check Over 2.5
                if (prediction.Over25Probability > 0.60 && (match.Odds?.Over25 ?? 0) >= (decimal)minOdds)
                {
                    bool won = (match.FTHG + match.FTAG) > 2.5;
                    goalsCandidates.Add((upcomingDto, match.Odds!.Over25, won, "Over 2.5"));
                }
                // Check BTTS (If odds available)
                else if (prediction.BTTSProbability > 0.60 && (match.Odds?.BttsYes ?? 0) >= (decimal)minOdds)
                {
                    bool won = (match.FTHG > 0 && match.FTAG > 0);
                    goalsCandidates.Add((upcomingDto, match.Odds!.BttsYes, won, "BTTS"));
                }
                // Check Home Win
                else if (prediction.HomeWinProbability > 0.60 && (match.Odds?.HomeWin ?? 0) >= (decimal)minOdds)
                {
                    bool won = match.FTR == "H";
                    hdwCandidates.Add((upcomingDto, match.Odds!.HomeWin, won, "Home Win"));
                }
            }
            
            var usedMatches = new HashSet<string>();
            var tickets = new List<List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>>();

            // Build Ticket 1 (HDW + 2 Goals)
            // Strategy: Try to pick 1 HDW (best odds or random? Take First).
            var hdwPick = hdwCandidates.FirstOrDefault(); 
            
            if (hdwPick.Match != null)
            {
                var t1 = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
                t1.Add(hdwPick);
                usedMatches.Add(GetMatchId(hdwPick.Match));
                
                // Fill with 2 goals
                foreach (var g in goalsCandidates)
                {
                    if (t1.Count >= 3) break;
                    if (!usedMatches.Contains(GetMatchId(g.Match)))
                    {
                        t1.Add(g);
                        usedMatches.Add(GetMatchId(g.Match));
                    }
                }
                
                if (t1.Count == 3) tickets.Add(t1);
            }

            // Build Ticket 2 (Goals)
            var t2 = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
            foreach (var g in goalsCandidates)
            {
                if (t2.Count >= 3) break;
                if (!usedMatches.Contains(GetMatchId(g.Match)))
                {
                    t2.Add(g);
                    usedMatches.Add(GetMatchId(g.Match));
                }
            }
            if (t2.Count == 3) tickets.Add(t2);

            // Build Ticket 3 (Goals)
            var t3 = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
            foreach (var g in goalsCandidates)
            {
                if (t3.Count >= 3) break;
                if (!usedMatches.Contains(GetMatchId(g.Match)))
                {
                    t3.Add(g);
                    usedMatches.Add(GetMatchId(g.Match));
                }
            }
            if (t3.Count == 3) tickets.Add(t3);

            // Do we have 3 tickets?
            if (tickets.Count < 3) continue;

            // Found a valid day!
            totalDaysFound++;
            portfolioReport.AppendLine($"## Date: {date:yyyy-MM-dd}");
            
            double dayProfit = 0;
            int tIndex = 0;
            foreach (var t in tickets)
            {
                tIndex++;
                bool won = t.All(leg => leg.Won);
                double odds = t.Aggregate(1.0, (acc, leg) => acc * (double)leg.Odds);
                string res = won ? "**WON**" : "LOST";
                double profit = won ? (odds * 100) - 100 : -100;
                dayProfit += profit;

                portfolioReport.AppendLine($"### Ticket #{tIndex} ({res}) - Odds: {odds:F2} - Profit: {profit:F0}€");
                foreach (var leg in t)
                {
                     portfolioReport.AppendLine($"- {leg.Selection} @ {leg.Odds}: {leg.Match.HomeTeam} vs {leg.Match.AwayTeam} ({(leg.Won ? "WON":"LOST")})");
                }
                portfolioReport.AppendLine("");
            }
            portfolioReport.AppendLine($"**Day Profit**: {dayProfit:F0}€");
            portfolioReport.AppendLine("---");
            totalPortfolioProfit += dayProfit;
        }

        portfolioReport.AppendLine($"# Summary");
        portfolioReport.AppendLine($"Days Found: {totalDaysFound}");
        portfolioReport.AppendLine($"Total Net Profit: {totalPortfolioProfit:F0}€");
        
        if (totalDaysFound == 0) return "No days found where 3 unique tickets could be formed with strict criteria.";

        return portfolioReport.ToString();
    }

    private string GetMatchId(UpcomingMatchDto m) => $"{m.HomeTeam}-{m.AwayTeam}-{m.Date}";

    public async Task<string> RunWeeklyPortfolioBacktestAsync(int weeks = 15, double minOdds = 1.40)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Weekly Portfolio Backtest from {Start} to {End}", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches.Where(m => m.Date >= startDate).OrderBy(m => m.Date).ToList();
        
        // Group by ISO Week (Year-Week)
        var groupedByWeek = testSet
            .GroupBy(m => new { Year = m.Date.Year, Week = System.Globalization.ISOWeek.GetWeekOfYear(m.Date) })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week);

        var portfolioReport = new StringBuilder();
        portfolioReport.AppendLine($"# Weekly Portfolio Backtest (Last {weeks} Weeks)");
        portfolioReport.AppendLine($"Strategy: 3 Tickets Per Week | Mixed Days Allowed | Min Odds {minOdds}");
        portfolioReport.AppendLine("Ticket 1: 1x HDW + 2x Goals | Ticket 2 & 3: 3x Goals (BTTS/Over)");
        portfolioReport.AppendLine("");

        int totalWeeksFound = 0;
        double totalPortfolioProfit = 0;
        double totalStake = 0;

        foreach (var weekGroup in groupedByWeek)
        {
            var weekInfo = weekGroup.Key;
            var weekMatches = weekGroup.ToList();
            var weekStart = weekMatches.Min(m => m.Date);
            var weekEnd = weekMatches.Max(m => m.Date);
            
            // Pools
            var hdwCandidates = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection, float Confidence)>();
            var goalsCandidates = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection, float Confidence)>();

            foreach (var match in weekMatches)
            {
                var upcomingDto = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"),
                    Time = match.Date.ToString("HH:mm"),
                    Odds = match.Odds != null ? new MatchOdds 
                    { 
                        Over25 = match.Odds.Over25, 
                        HomeWin = match.Odds.HomeWin,
                        AwayWin = match.Odds.AwayWin,
                        BttsYes = match.Odds.BttsYes
                    } : null
                };

                // We need prediction
                var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
                var prediction = await mlService.PredictMatchAsync(upcomingDto, pastHistory);
                if (prediction == null) continue;

                // Check Over 2.5
                if (prediction.Over25Probability > 0.60 && (match.Odds?.Over25 ?? 0) >= (decimal)minOdds)
                {
                    bool won = (match.FTHG + match.FTAG) > 2.5;
                    goalsCandidates.Add((upcomingDto, match.Odds!.Over25, won, "Over 2.5", prediction.Over25Probability));
                }
                // Check BTTS
                else if (prediction.BTTSProbability > 0.60 && (match.Odds?.BttsYes ?? 0) >= (decimal)minOdds)
                {
                    bool won = (match.FTHG > 0 && match.FTAG > 0);
                    goalsCandidates.Add((upcomingDto, match.Odds!.BttsYes, won, "BTTS", prediction.BTTSProbability));
                }
                // Check Home Win
                else if (prediction.HomeWinProbability > 0.60 && (match.Odds?.HomeWin ?? 0) >= (decimal)minOdds)
                {
                    bool won = match.FTR == "H";
                    hdwCandidates.Add((upcomingDto, match.Odds!.HomeWin, won, "Home Win", prediction.HomeWinProbability));
                }
            }
            
            // Sort Candidates by Confidence/Odds? "Best 3 tickets".
            // Let's sort by Confidence descending.
            hdwCandidates = hdwCandidates.OrderByDescending(x => x.Confidence).ToList();
            goalsCandidates = goalsCandidates.OrderByDescending(x => x.Confidence).ToList();

            var usedMatches = new HashSet<string>();
            var tickets = new List<List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>>();

            // Ticket 1: 1 HDW + 2 Goals
            var hdwPick = hdwCandidates.FirstOrDefault();
            if (hdwPick.Match != null)
            {
                var t1 = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
                t1.Add((hdwPick.Match, hdwPick.Odds, hdwPick.Won, hdwPick.Selection));
                usedMatches.Add(GetMatchId(hdwPick.Match));
                
                foreach (var g in goalsCandidates)
                {
                    if (t1.Count >= 3) break;
                    if (!usedMatches.Contains(GetMatchId(g.Match)))
                    {
                        t1.Add((g.Match, g.Odds, g.Won, g.Selection));
                        usedMatches.Add(GetMatchId(g.Match));
                    }
                }
                if (t1.Count == 3) tickets.Add(t1);
            }

            // Ticket 2 & 3: Goals only
            for (int i=0; i<2; i++)
            {
                var t = new List<(UpcomingMatchDto Match, decimal Odds, bool Won, string Selection)>();
                foreach (var g in goalsCandidates)
                {
                    if (t.Count >= 3) break;
                    if (!usedMatches.Contains(GetMatchId(g.Match)))
                    {
                        t.Add((g.Match, g.Odds, g.Won, g.Selection));
                        usedMatches.Add(GetMatchId(g.Match));
                    }
                }
                if (t.Count == 3) tickets.Add(t);
            }

            if (tickets.Count < 3) continue; // Skip week if not enough bets

            totalWeeksFound++;
            portfolioReport.AppendLine($"## Week {weekInfo.Year}-W{weekInfo.Week:00} ({weekStart:MM/dd} - {weekEnd:MM/dd})");

            double weekProfit = 0;
            int tIndex = 0;
            foreach (var t in tickets)
            {
                tIndex++;
                bool won = t.All(leg => leg.Won);
                double odds = t.Aggregate(1.0, (acc, leg) => acc * (double)leg.Odds);
                string res = won ? "**WON**" : "LOST";
                double profit = won ? (odds * 100) - 100 : -100;
                weekProfit += profit;
                totalStake += 100;

                portfolioReport.AppendLine($"### Ticket #{tIndex} ({res}) - Odds: {odds:F2} - Stake: 100€ - Profit: {profit:F0}€");
                foreach (var leg in t)
                {
                     portfolioReport.AppendLine($"- {leg.Selection} @ {leg.Odds}: {leg.Match.HomeTeam} vs {leg.Match.AwayTeam} ({leg.Match.Date})");
                }
                portfolioReport.AppendLine("");
            }
            portfolioReport.AppendLine($"**Week Profit**: {weekProfit:F0}€");
            portfolioReport.AppendLine("---");
            totalPortfolioProfit += weekProfit;
        }

        double roi = totalStake > 0 ? (totalPortfolioProfit / totalStake) * 100 : 0;
        portfolioReport.AppendLine($"# Summary");
        portfolioReport.AppendLine($"Weeks Processed: {totalWeeksFound}");
        portfolioReport.AppendLine($"Total Stake: {totalStake:F0}€");
        portfolioReport.AppendLine($"Total Net Profit: {totalPortfolioProfit:F0}€");
        portfolioReport.AppendLine($"ROI: {roi:F2}%");

        return portfolioReport.ToString();
    }
}
