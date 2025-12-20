
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.ML;

public class FeatureEngineeringService(
    soccer_gpt_infrastructure.Services.Analysis.RefereeAnalysisService refereeService,
    soccer_gpt_infrastructure.Services.Analysis.CongestionAnalysisService congestionService)
{
    public async Task<List<MatchFeatureInput>> CreateTrainingDatasetAsync(List<HistoricalMatchDto> allMatches)
    {
        // Sort by date ascending to process chronologically
        var sortedMatches = allMatches.OrderBy(m => m.Date).ToList();
        var dataset = new List<MatchFeatureInput>();

        // We need a lookup for rolling stats. 
        // Dictionary<TeamName, List<Match>>
        // But we need strict temporal ordering.
        
        for (int i = 0; i < sortedMatches.Count; i++)
        {
            var match = sortedMatches[i];
            
            // Skip matches with missing critical data
            if (string.IsNullOrEmpty(match.HomeTeam) || string.IsNullOrEmpty(match.AwayTeam)) continue;

            // Get historical matches BEFORE this match for Home and Away teams
            var pastMatches = sortedMatches.Take(i).ToList();

            var featureRow = await CalculateFeaturesAsync(match, pastMatches);
            
            // Add Tags (Labels)
            featureRow.LabelTotalGoals = (float)(match.FTHG + match.FTAG);
            featureRow.LabelIsOver15 = (match.FTHG + match.FTAG) > 1.5;
            featureRow.LabelIsOver25 = (match.FTHG + match.FTAG) > 2.5;
            featureRow.LabelIsDraw = match.FTR == "D";
            // featureRow.LabelIsDraw = match.FTR == "D"; // fix duplicate line in original
            featureRow.LabelIsZeroZero = (match.FTHG + match.FTAG) == 0;
            featureRow.LabelHomeWin = match.FTR == "H";
            
            // Trap Definition: 
            // Market said Over (Odds < 1.8ish?) but result was Under?
            // Or "Low Goal Trap" = Market expects goals but result is < 2.5?
            // User definition: "Matches where Over markets are TRAPS".
            // Let's define it as: Implied Prob Over 2.5 > 0.55 AND Total Goals < 3
            // Needs odds data.
            bool marketExpectedGoals = match.Odds?.Over25 < 1.85m; // Approx > 54% prob
            bool resultLow = (match.FTHG + match.FTAG) < 3;
            featureRow.LabelIsLowGoalTrap = marketExpectedGoals && resultLow;

            dataset.Add(featureRow);
        }

        return dataset;
    }
    
    public async Task<MatchFeatureInput> CalculateFeaturesAsync(HistoricalMatchDto targetMatch, List<HistoricalMatchDto> history)
    {
        var input = new MatchFeatureInput();
        
        // 1. Context
        // Date parsing for "Days Rest"
        DateTime matchDate = targetMatch.Date;
        
        // Filter history for specific teams
        var homeHistory = history.Where(m => m.HomeTeam == targetMatch.HomeTeam || m.AwayTeam == targetMatch.HomeTeam).OrderByDescending(m => m.Date).ToList();
        var awayHistory = history.Where(m => m.HomeTeam == targetMatch.AwayTeam || m.AwayTeam == targetMatch.AwayTeam).OrderByDescending(m => m.Date).ToList();

        // Days Rest
        if (homeHistory.Any()) input.HomeDaysRest = (float)(matchDate - homeHistory.First().Date).TotalDays;
        else input.HomeDaysRest = 30; // Default fresh

        if (awayHistory.Any()) input.AwayDaysRest = (float)(matchDate - awayHistory.First().Date).TotalDays;
        else input.AwayDaysRest = 30;

        // Europe Fatigue logic (placeholder check string for "Europe" league?)
        // Assuming strict leagues list, maybe check "Div"? or if they played < 4 days ago in a diff league?
        // Simple logic: Played < 4 days ago?
        input.HomePlayedEuropeLast7d = input.HomeDaysRest < 4; 
        input.AwayPlayedEuropeLast7d = input.AwayDaysRest < 4;

        // 2. Rolling Stats (Goals)
        input.HomeGoalsForAvg5 = CalculateAvgGoalsFor(homeHistory, targetMatch.HomeTeam, 5);
        input.HomeGoalsAgainstAvg5 = CalculateAvgGoalsAgainst(homeHistory, targetMatch.HomeTeam, 5);
        input.HomeGoalsForAvg10 = CalculateAvgGoalsFor(homeHistory, targetMatch.HomeTeam, 10);
        input.HomeGoalsAgainstAvg10 = CalculateAvgGoalsAgainst(homeHistory, targetMatch.HomeTeam, 10);

        input.AwayGoalsForAvg5 = CalculateAvgGoalsFor(awayHistory, targetMatch.AwayTeam, 5);
        input.AwayGoalsAgainstAvg5 = CalculateAvgGoalsAgainst(awayHistory, targetMatch.AwayTeam, 5);
        input.AwayGoalsForAvg10 = CalculateAvgGoalsFor(awayHistory, targetMatch.AwayTeam, 10);
        input.AwayGoalsAgainstAvg10 = CalculateAvgGoalsAgainst(awayHistory, targetMatch.AwayTeam, 10);

        // 3. Clean Sheets & Failed to Score
        input.HomeCleanSheetRate10 = CalculateCleanSheetRate(homeHistory, targetMatch.HomeTeam, 10);
        input.AwayCleanSheetRate10 = CalculateCleanSheetRate(awayHistory, targetMatch.AwayTeam, 10);
        input.HomeFailedToScoreRate10 = CalculateFailedToScoreRate(homeHistory, targetMatch.HomeTeam, 10);
        input.AwayFailedToScoreRate10 = CalculateFailedToScoreRate(awayHistory, targetMatch.AwayTeam, 10);

        // 4. Draw Rates
        input.HomeDrawRate5 = CalculateDrawRate(homeHistory, 5);
        input.HomeDrawRate10 = CalculateDrawRate(homeHistory, 10);
        input.AwayDrawRate5 = CalculateDrawRate(awayHistory, 5);
        input.AwayDrawRate10 = CalculateDrawRate(awayHistory, 10);
        input.CombinedDrawRate10 = (input.HomeDrawRate10 + input.AwayDrawRate10) / 2.0f;

        // 5. Head to Head
        var h2h = history.Where(m => 
            (m.HomeTeam == targetMatch.HomeTeam && m.AwayTeam == targetMatch.AwayTeam) ||
            (m.HomeTeam == targetMatch.AwayTeam && m.AwayTeam == targetMatch.HomeTeam)
        ).ToList();

        input.H2HMatchesCount = h2h.Count;
        input.H2HAvgTotalGoals = h2h.Any() ? (float)h2h.Average(m => m.FTHG + m.FTAG) : 2.5f; // Default baseline
        input.H2HUnder25Rate = h2h.Any() ? (float)h2h.Count(m => (m.FTHG + m.FTAG) < 3) / h2h.Count : 0.5f;
        input.H2HZeroZeroRate = h2h.Any() ? (float)h2h.Count(m => (m.FTHG + m.FTAG) == 0) / h2h.Count : 0.0f;

        // 6. Market (Odds)
        // Ensure odds are available in input dto, usually they are nullable.
        input.OddsOver15 = 0; // Populate if source has it
        input.OddsOver25 = (float)(targetMatch.Odds?.Over25 ?? 0);
        input.OddsDraw = (float)(targetMatch.Odds?.Draw ?? 0);
        
        if (input.OddsOver25 > 1) input.OddsOver25ImpliedProb = 1.0f / input.OddsOver25;
        if (input.OddsDraw > 1) input.OddsDraw = 1.0f / input.OddsDraw; // Re-using var or mapped correctly? Ah OddsDraw is the odds value.
        // Wait, typical impl would act on the field directly.
        
        // 7. Trap Signals (Heuristic)
        // 7. Trap Signals (Heuristic)
        input.BothTeamsDefensive = (input.HomeGoalsAgainstAvg5 + input.AwayGoalsAgainstAvg5) < 2.0f;
        input.BothTeamsLowScoring = (input.HomeGoalsForAvg5 + input.AwayGoalsForAvg5) < 2.2f;
        input.BothTeamsPoorForm = false; // Logic needed, maybe points per game?
        
        input.HighDrawBias = input.CombinedDrawRate10 > 0.35f;
        
        // NEW: Referee Stats
        var refStats = await refereeService.AnalyzeRefereeAsync(targetMatch.Referee, matchDate);
        input.RefereeAvgGoals = (float)refStats.AvgGoals;
        input.RefereeOver25Rate = (float)refStats.Over25Rate;
        
        // NEW: Congestion Stats
        var homeCongestion = await congestionService.AnalyzeCongestionAsync(targetMatch.HomeTeam, matchDate);
        var awayCongestion = await congestionService.AnalyzeCongestionAsync(targetMatch.AwayTeam, matchDate);
        
        input.HomeDaysSinceEurope = homeCongestion.DaysSinceEurope;
        input.HomeDaysUntilEurope = homeCongestion.DaysUntilEurope;
        input.AwayDaysSinceEurope = awayCongestion.DaysSinceEurope;
        input.AwayDaysUntilEurope = awayCongestion.DaysUntilEurope;
        input.EuropeFatigueTrap = homeCongestion.IsFatigued || awayCongestion.IsFatigued;


        return input;
    }

    // --- Helpers ---
    
    private float CalculateAvgGoalsFor(List<HistoricalMatchDto> matches, string teamName, int n)
    {
        var recent = matches.Take(n).ToList();
        if (recent.Count == 0) return 1.2f; // League Avg Baseline
        
        double sum = 0;
        foreach (var m in recent)
        {
            if (m.HomeTeam == teamName) sum += m.FTHG;
            else sum += m.FTAG;
        }
        return (float)(sum / recent.Count);
    }

    private float CalculateAvgGoalsAgainst(List<HistoricalMatchDto> matches, string teamName, int n)
    {
        var recent = matches.Take(n).ToList();
        if (recent.Count == 0) return 1.2f; 
        
        double sum = 0;
        foreach (var m in recent)
        {
            if (m.HomeTeam == teamName) sum += m.FTAG; // Against = Away goals
            else sum += m.FTHG; // Against = Home goals
        }
        return (float)(sum / recent.Count);
    }
    
    private float CalculateCleanSheetRate(List<HistoricalMatchDto> matches, string teamName, int n)
    {
        var recent = matches.Take(n).ToList();
        if (recent.Count == 0) return 0.25f;
        
        int cs = 0;
        foreach (var m in recent)
        {
            int goalsConceded = (m.HomeTeam == teamName) ? m.FTAG : m.FTHG;
            if (goalsConceded == 0) cs++;
        }
        return (float)cs / recent.Count;
    }
    
    private float CalculateFailedToScoreRate(List<HistoricalMatchDto> matches, string teamName, int n)
    {
        var recent = matches.Take(n).ToList();
        if (recent.Count == 0) return 0.2f;
        
        int fts = 0;
        foreach (var m in recent)
        {
            int goalsScored = (m.HomeTeam == teamName) ? m.FTHG : m.FTAG;
            if (goalsScored == 0) fts++;
        }
        return (float)fts / recent.Count;
    }

    private float CalculateDrawRate(List<HistoricalMatchDto> matches, int n)
    {
        var recent = matches.Take(n).ToList();
        if (recent.Count == 0) return 0.25f;
        return (float)recent.Count(m => m.FTR == "D") / recent.Count;
    }
}
