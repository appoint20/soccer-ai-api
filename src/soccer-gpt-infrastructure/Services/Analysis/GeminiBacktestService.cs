using System.Text;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.Analysis;

public class GeminiBacktestService(
    IHistoricalDataRepository historicalRepository,
    IMlPredictionService mlPredictionService,
    ITeamStatsService teamStatsService,
    ITrapDetectionService trapDetectionService,
    IAdvancedStatsService advancedStatsService,
    IGeminiAnalysisService geminiAnalysisService,
    ILogger<GeminiBacktestService> logger)
{
    public async Task<string> RunBacktestAsync(int weeks = 15, int maxMatchesPerWeek = 4)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Gemini Backtest from {Start} to {End} (Limit {Limit}/week)", 
            startDate.ToShortDateString(), lastDate.ToShortDateString(), maxMatchesPerWeek);

        var testSet = allMatches
            .Where(m => m.Date >= startDate)
            .OrderBy(m => m.Date)
            .ToList();
        
        // Group by ISO Week to batch properly
        var groupedByWeek = testSet
            .GroupBy(m => new { Year = m.Date.Year, Week = System.Globalization.ISOWeek.GetWeekOfYear(m.Date) })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Gemini Batch Analysis Backtest (Last {weeks} Weeks)");
        sb.AppendLine($"Date Range: {startDate:d} - {lastDate:d}");
        sb.AppendLine($"Detailed Analysis: Top {maxMatchesPerWeek} matches per week analyzed by Gemini");
        sb.AppendLine("");

        int totalDocsAnalyzed = 0;
        int correctPredictions = 0;
        int correctOver25 = 0;
        int correctBTTS = 0;
        int totalOver25Preds = 0;
        int totalBTTSPreds = 0;

        foreach (var weekGroup in groupedByWeek)
        {
            var weekInfo = weekGroup.Key;
            var weekStart = weekGroup.Min(m => m.Date);
            var weekEnd = weekGroup.Max(m => m.Date);

            // Select best matches for this week (e.g. have odds, top leagues, interesting H2H)
            // To be fair and interesting, lets pick matches where ML model shows some confidence or Odds exist.
            // Simplified: Take first N matches with Odds.
            var weekBatch = weekGroup
                .Where(m => m.Odds != null && !string.IsNullOrEmpty(m.FTR))
                .Take(maxMatchesPerWeek)
                .ToList();

            if (!weekBatch.Any()) continue;
            
            sb.AppendLine($"### Week {weekInfo.Week} ({weekStart:MM/dd} - {weekEnd:MM/dd})");

            // 1. Enrich Data (Three-Pass Pass 1 equivalent)
            var preparedMatches = new List<UpcomingMatchDto>();
            var matchContexts = new Dictionary<string, HistoricalMatchDto>(); // Keep track of actual result

            foreach (var match in weekBatch)
            {
                var matchId = $"{match.HomeTeam}-{match.AwayTeam}-{match.Date:yyyy-MM-dd}"; // Format matches Prompt
                matchContexts[matchId] = match;

                // STRICTLY PAST HISTORY
                var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
                
                // Calculate all components
                var upcomingDto = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"), // Format matches Prompt
                    Time = match.Date.ToString("HH:mm"),
                    LeagueName = match.League ?? "Unknown",
                    Odds = match.Odds != null ? new MatchOdds 
                    { 
                        Over25 = match.Odds.Over25, 
                        HomeWin = match.Odds.HomeWin, 
                        AwayWin = match.Odds.AwayWin,
                        BttsYes = match.Odds.BttsYes
                    } : null
                };

                // Stats
                upcomingDto = upcomingDto with
                {
                    HomeTeamStats = await teamStatsService.CalculateStatsAsync(match.HomeTeam, pastHistory),
                    AwayTeamStats = await teamStatsService.CalculateStatsAsync(match.AwayTeam, pastHistory)
                };

                // Advanced
                var advanced = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, pastHistory);
                upcomingDto = upcomingDto with { AdvancedAnalytics = advanced };

                // Traps
                var traps = trapDetectionService.AnalyzeTraps(upcomingDto, advanced);
                upcomingDto = upcomingDto with { Traps = traps };

                // ML
                var mlPred = await mlPredictionService.PredictMatchAsync(upcomingDto, pastHistory);
                upcomingDto = upcomingDto with { MlPrediction = mlPred };
                
                // H2H Analysis (Basic impl for now)
                var h2hMatches = pastHistory.Where(m => 
                    (m.HomeTeam == match.HomeTeam && m.AwayTeam == match.AwayTeam) ||
                    (m.HomeTeam == match.AwayTeam && m.AwayTeam == match.HomeTeam)).OrderByDescending(m => m.Date).Take(5).ToList();
                
                upcomingDto = upcomingDto with
                {
                    H2HAnalysis = new H2HAnalysis
                    {
                        HomeWinsLast5 = h2hMatches.Count(m => (m.HomeTeam == match.HomeTeam && m.FTR == "H") || (m.AwayTeam == match.HomeTeam && m.FTR == "A")),
                        AwayWinsLast5 = h2hMatches.Count(m => (m.HomeTeam == match.AwayTeam && m.FTR == "A") || (m.AwayTeam == match.AwayTeam && m.FTR == "H")),
                        DrawsLast5 = h2hMatches.Count(m => m.FTR == "D"),
                        AvgGoalsHome = upcomingDto.HomeTeamStats.AvgGoalsFor,
                        AvgGoalsAway = upcomingDto.AwayTeamStats.AvgGoalsFor
                    }
                };

                preparedMatches.Add(upcomingDto);
            }

            // 2. call Gemini (Pass 2)
            // Group by League (though likely mixed, we'll just respect the service signature)
            var leagueGroups = preparedMatches.GroupBy(m => m.LeagueName);
            
            foreach (var leagueGroup in leagueGroups)
            {
                var analyses = await geminiAnalysisService.AnalyzeMatchBatchAsync(leagueGroup.Key, leagueGroup.ToList());

                // 3. Evaluate (Pass 3)
                foreach (var (key, analysis) in analyses)
                {
                     // Match ID format in AnalysisService is Home-Away-Date? 
                     // Wait, AnalyzeMatchBatchAsync returns dictionary key Home-Away. 
                     // But inside GeminiAnalysisService, it tries to match MatchId from prompt.
                     // Service returns Key: $"{match.HomeTeam}-{match.AwayTeam}"
                     // We need to match this back to our `matchContexts`.

                    // Reconstruct Key to find original match
                    // The service returns Dictionary<string, GeminiMatchAnalysis> where string is "Home-Away"
                    // But we have multiple matches potentially? No, "Home-Away" is unique enough for a week usually.
                    
                    var matchingContext = matchContexts.Values.FirstOrDefault(m => 
                        $"{m.HomeTeam}-{m.AwayTeam}" == key
                    );

                    if (matchingContext == null) continue;

                    totalDocsAnalyzed++;
                    bool isCorrect = false;
                    string resultStr = $"{matchingContext.FTHG}-{matchingContext.FTAG} ({matchingContext.FTR})";

                    // Simple Parsing of Prediction String
                    var p = analysis.Prediction.ToLower();
                    
                    if (p.Contains("over 2.5"))
                    {
                        totalOver25Preds++;
                        if ((matchingContext.FTHG + matchingContext.FTAG) > 2.5)
                        {
                            correctPredictions++;
                            correctOver25++;
                            isCorrect = true;
                        }
                    }
                    else if (p.Contains("btts"))
                    {
                        totalBTTSPreds++;
                        if (matchingContext.FTHG > 0 && matchingContext.FTAG > 0)
                        {
                            correctPredictions++;
                            correctBTTS++;
                            isCorrect = true;
                        }
                    }
                    else if (p.Contains("home win"))
                    {
                        if (matchingContext.FTR == "H") { correctPredictions++; isCorrect = true; }
                    }
                    else if (p.Contains("away win"))
                    {
                        if (matchingContext.FTR == "A") { correctPredictions++; isCorrect = true; }
                    }
                    else if (p.Contains("draw"))
                    {
                        if (matchingContext.FTR == "D") { correctPredictions++; isCorrect = true; }
                    }

                    string icon = isCorrect ? "✅" : "❌";
                    sb.AppendLine($"- {icon} **{matchingContext.HomeTeam} vs {matchingContext.AwayTeam}**: Predicted **{analysis.Prediction}** (Conf: {analysis.ConfidenceLevel:F2}) | Result: {resultStr}");
                }
            }
            sb.AppendLine("");
            
            // Respect Rate Limits: 20 seconds delay per week
            await Task.Delay(20000);
        }
        
        double accuracy = totalDocsAnalyzed > 0 ? (double)correctPredictions / totalDocsAnalyzed * 100 : 0;
        double accOver25 = totalOver25Preds > 0 ? (double)correctOver25 / totalOver25Preds * 100 : 0;
        double accBTTS = totalBTTSPreds > 0 ? (double)correctBTTS / totalBTTSPreds * 100 : 0;

        sb.AppendLine("## Summary");
        sb.AppendLine($"Total Matches Analyzed by Gemini: {totalDocsAnalyzed}");
        sb.AppendLine($"Overall Accuracy: {accuracy:F1}% ({correctPredictions}/{totalDocsAnalyzed})");
        sb.AppendLine($"Over 2.5 Accuracy: {accOver25:F1}% ({correctOver25}/{totalOver25Preds})");
        sb.AppendLine($"BTTS Accuracy: {accBTTS:F1}% ({correctBTTS}/{totalBTTSPreds})");

        return sb.ToString();
    }
    public async Task<string> RunFailureAnalysisAsync(int weeks = 15)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running ML Failure Analysis from {Start} to {End}", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches
            .Where(m => m.Date >= startDate && m.Odds != null) // Only matches with odds
            .OrderBy(m => m.Date)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# ML Prediction Failure Analysis (Last {weeks} Weeks)");
        sb.AppendLine($"Date Range: {startDate:d} - {lastDate:d}");
        sb.AppendLine("Focus: Analyzing matches where ML Model had High Confidence (>60%) but FAILED.");
        sb.AppendLine("");

        int totalAnalyzed = 0;
        int failuresOver25 = 0;
        
        // Group by Week just for structure
        var groupedByWeek = testSet
            .GroupBy(m => new { Year = m.Date.Year, Week = System.Globalization.ISOWeek.GetWeekOfYear(m.Date) })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week);

        foreach (var weekGroup in groupedByWeek)
        {
            var weekFailures = new StringBuilder();
            bool hasFailures = false;

            foreach (var match in weekGroup)
            {
                // Simple Check: Over 2.5 Goals Failure
                // Create Context
                var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
                
                var upcomingDto = new UpcomingMatchDto
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date.ToString("yyyy-MM-dd"),
                    Odds = new MatchOdds { Over25 = match.Odds!.Over25, HomeWin = match.Odds.HomeWin, AwayWin = match.Odds.AwayWin }
                };

                var mlPred = await mlPredictionService.PredictMatchAsync(upcomingDto, pastHistory);
                if (mlPred == null || mlPred.Over25Probability < 0.60) continue; // Skip if models weren't confident

                // Model said YES (>60%), did it happen?
                bool actualOver25 = (match.FTHG + match.FTAG) > 2.5;

                if (!actualOver25) // FAILURE DETECTED
                {
                    totalAnalyzed++;
                    failuresOver25++;
                    hasFailures = true;

                    // Deep Dive: Why did it fail?
                    var advanced = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, pastHistory);
                    var traps = trapDetectionService.AnalyzeTraps(upcomingDto, advanced);
                    var teamStatsH = await teamStatsService.CalculateStatsAsync(match.HomeTeam, pastHistory);
                    var teamStatsA = await teamStatsService.CalculateStatsAsync(match.AwayTeam, pastHistory);

                    weekFailures.AppendLine($"### ❌ {match.HomeTeam} vs {match.AwayTeam} ({match.FTHG}-{match.FTAG})");
                    weekFailures.AppendLine($"- **ML Confidence**: {mlPred.Over25Probability:P1} (Expected Goals: {mlPred.ExpectedGoals:F2})");
                    
                    // Contradictions Check
                    var contradictions = new List<string>();
                    
                    // 1. Math Model Contradiction?
                    if (advanced.Probabilities.Over25 < 0.50) 
                        contradictions.Add($"📉 **Poisson/DC Disagreed**: Only {advanced.Probabilities.Over25:P1} chance of O2.5");
                    
                    // 2. Monte Carlo Contradiction?
                    if (advanced.StreakAnalysis.EdgeOver25 < 0.50)
                        contradictions.Add($"🎲 **Monte Carlo Disagreed**: Sim showed only {advanced.StreakAnalysis.EdgeOver25:P1} O2.5");

                    // 3. Form Contradiction?
                    if (teamStatsH.AvgGoalsFor < 1.0 || teamStatsA.AvgGoalsFor < 1.0)
                        contradictions.Add($"🐌 **Form Warning**: Low Avg Goals ({match.HomeTeam}: {teamStatsH.AvgGoalsFor:F1}, {match.AwayTeam}: {teamStatsA.AvgGoalsFor:F1})");

                    // 4. Traps?
                    if (traps.Any())
                        contradictions.Add($"🪤 **Traps Detected**: {string.Join(", ", traps)}");

                    // 5. Odds (Market)
                    if (match.Odds.Over25 > 1.90m)
                         contradictions.Add($"💰 **Market Skepticism**: High Odds for O2.5 ({match.Odds.Over25})");

                    if (contradictions.Count > 0)
                    {
                        weekFailures.AppendLine("- **Why it might have failed:**");
                        foreach (var c in contradictions) weekFailures.AppendLine($"  - {c}");
                    }
                    else
                    {
                        weekFailures.AppendLine("- **Mystery Failure**: All signals aligned (ML, Math, Stats), but game went Under.");
                    }
                    weekFailures.AppendLine("");
                }
            }

            if (hasFailures)
            {
                sb.AppendLine($"## Week {weekGroup.Key.Year}-W{weekGroup.Key.Week:00}");
                sb.Append(weekFailures);
            }
        }
        
        sb.AppendLine("## Summary");
        sb.AppendLine($"Total High Confidence Failures Analyzed: {failuresOver25}");
        if (totalAnalyzed > 0)
             sb.AppendLine($"This report analyzed {totalAnalyzed} matches where the model was WRONG to find patterns.");

        return sb.ToString();
    }

    public async Task<string> RunLowScoreAnalysisAsync(int weeks = 15)
    {
        var allMatches = await historicalRepository.GetAllMatchesAsync();
        if (allMatches.Count == 0) return "No historical data found.";

        var lastDate = allMatches.Max(m => m.Date);
        var startDate = lastDate.AddDays(-(weeks * 7));
        
        logger.LogInformation("Running Low Score (1-0/0-1) Analysis from {Start} to {End}", startDate.ToShortDateString(), lastDate.ToShortDateString());

        var testSet = allMatches
            .Where(m => m.Date >= startDate && ((m.FTHG == 1 && m.FTAG == 0) || (m.FTHG == 0 && m.FTAG == 1)))
            .OrderBy(m => m.Date)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Low Score (1-0 / 0-1) Pattern Analysis (Last {weeks} Weeks)");
        sb.AppendLine($"Date Range: {startDate:d} - {lastDate:d}");
        sb.AppendLine($"Total Matches Found: {testSet.Count}");
        sb.AppendLine("Focus: What signals did we miss? Why did these games stay Under 1.5/2.5?");
        sb.AppendLine("");

        int highConfMlFailures = 0; // ML said YES to O2.5, but game was 1-0/0-1
        int mlCorrectlyPredictedUnder = 0; // ML said NO to O2.5

        foreach (var match in testSet)
        {
            // Context
            var pastHistory = allMatches.Where(m => m.Date < match.Date).ToList();
            
            var upcomingDto = new UpcomingMatchDto
            {
                HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam,
                Date = match.Date.ToString("yyyy-MM-dd"),
                Odds = match.Odds != null ? new MatchOdds { Over25 = match.Odds.Over25, HomeWin = match.Odds.HomeWin, AwayWin = match.Odds.AwayWin } : null
            };

            var mlPred = await mlPredictionService.PredictMatchAsync(upcomingDto, pastHistory);
            var advanced = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, pastHistory);
            var teamStatsH = await teamStatsService.CalculateStatsAsync(match.HomeTeam, pastHistory);
            var teamStatsA = await teamStatsService.CalculateStatsAsync(match.AwayTeam, pastHistory);

            bool mlHighConfOver = mlPred != null && mlPred.Over25Probability > 0.60;
            
            if (mlHighConfOver)
            {
                highConfMlFailures++;
                sb.AppendLine($"### ⚠️ {match.HomeTeam} vs {match.AwayTeam} ({match.FTHG}-{match.FTAG})");
                // Fixed format string
                sb.AppendLine($"- **ML ERROR**: Predicted High Prob O2.5 ({mlPred!.Over25Probability:P1})");
                
                // Indicators that might have saved us
                if (advanced.Probabilities.Over25 < 0.50) sb.AppendLine($"  - ✅ **Poisson Saved Us**: Low O2.5 Prob ({advanced.Probabilities.Over25:P1})");
                if (advanced.StreakAnalysis.EdgeOver25 < 0.50) sb.AppendLine($"  - ✅ **Monte Carlo Saved Us**: Low O2.5 Sim ({advanced.StreakAnalysis.EdgeOver25:P1})");
                
                double combinedAvgGoals = teamStatsH.AvgGoalsFor + teamStatsA.AvgGoalsFor;
                if (combinedAvgGoals < 2.5) sb.AppendLine($"  - ✅ **Stats Saved Us**: Combined Avg Goals {combinedAvgGoals:F2} < 2.5");
                
                if (match.Odds?.Over25 > 1.85m) sb.AppendLine($"  - ✅ **Market Saved Us**: High Odds {match.Odds.Over25}");
            }
            else
            {
                mlCorrectlyPredictedUnder++;
            }
        }
        
        sb.AppendLine("");
        sb.AppendLine("## Summary Statistics");
        sb.AppendLine($"Matches ending 1-0 or 0-1: {testSet.Count}");
        
        double correctPct = testSet.Count > 0 ? (double)mlCorrectlyPredictedUnder / testSet.Count : 0;
        double errorPct = testSet.Count > 0 ? (double)highConfMlFailures / testSet.Count : 0;
        
        sb.AppendLine($"ML Correctly Predicted Under/Low Conf: {mlCorrectlyPredictedUnder} ({correctPct:P1})");
        sb.AppendLine($"ML Incorrectly High Conf Over 2.5: {highConfMlFailures} ({errorPct:P1})");
        
        return sb.ToString();
    }
}
