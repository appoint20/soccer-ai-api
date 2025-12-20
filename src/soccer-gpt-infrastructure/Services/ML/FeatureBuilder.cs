using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.ML;

public class FeatureBuilder
{
    public List<MatchFeatureInput> BuildFeatures(List<HistoricalMatchDto> allMatches)
    {
        var features = new List<MatchFeatureInput>();

        // 1. Sort by Date to ensure no data leakage (processing in chronological order)
        var sortedMatches = allMatches.OrderBy(m => m.Date).ToList();

        // Track team history for rolling window calculations
        // Map: TeamName -> List of Matches (chronological)
        var teamHistory = new Dictionary<string, List<HistoricalMatchDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in sortedMatches)
        {
            var home = match.HomeTeam;
            var away = match.AwayTeam;
            var date = match.Date;

            // Ensure history lists exist
            if (!teamHistory.ContainsKey(home)) teamHistory[home] = new List<HistoricalMatchDto>();
            if (!teamHistory.ContainsKey(away)) teamHistory[away] = new List<HistoricalMatchDto>();

            var homeHistory = teamHistory[home];
            var awayHistory = teamHistory[away];

            // 2. Calculate Features using ONLY past history
            var f = new MatchFeatureInput();

            // === MATCH CONTEXT ===
            if (float.TryParse(match.Season, out var s)) f.Season = s;
            f.LeagueId = HashLeague(match.League); // Simple hash for ID or use a mapping service if available
            f.Round = 0; // Difficult to determine without full schedule knowledge, leaving as 0 or could estimate from match count
            
            // Days Rest
            var lastHome = homeHistory.LastOrDefault();
            var lastAway = awayHistory.LastOrDefault();
            f.HomeDaysRest = lastHome != null ? (float)(date - lastHome.Date).TotalDays : 7; // Default 7 if no history
            f.AwayDaysRest = lastAway != null ? (float)(date - lastAway.Date).TotalDays : 7;

            // European Fatigue (Simplified: if played < 4 days ago)
            f.HomePlayedEuropeLast7d = f.HomeDaysRest < 4; // Crude proxy if "League" doesn't distinguish Euro matches
            f.AwayPlayedEuropeLast7d = f.AwayDaysRest < 4;

            // === GOALS & DEFENSE (ROLLING) ===
            var h5 = homeHistory.TakeLast(5).ToList();
            var h10 = homeHistory.TakeLast(10).ToList();
            var a5 = awayHistory.TakeLast(5).ToList();
            var a10 = awayHistory.TakeLast(10).ToList();

            f.HomeGoalsForAvg5 = CalculateAvgGoals(h5, home, isFor: true);
            f.HomeGoalsAgainstAvg5 = CalculateAvgGoals(h5, home, isFor: false);
            f.HomeGoalsForAvg10 = CalculateAvgGoals(h10, home, isFor: true);
            f.HomeGoalsAgainstAvg10 = CalculateAvgGoals(h10, home, isFor: false);

            f.AwayGoalsForAvg5 = CalculateAvgGoals(a5, away, isFor: true);
            f.AwayGoalsAgainstAvg5 = CalculateAvgGoals(a5, away, isFor: false);
            f.AwayGoalsForAvg10 = CalculateAvgGoals(a10, away, isFor: true);
            f.AwayGoalsAgainstAvg10 = CalculateAvgGoals(a10, away, isFor: false);

            f.HomeCleanSheetRate10 = CalculateCleanSheetRate(h10, home);
            f.AwayCleanSheetRate10 = CalculateCleanSheetRate(a10, away);
            f.HomeFailedToScoreRate10 = CalculateFailedToScoreRate(h10, home);
            f.AwayFailedToScoreRate10 = CalculateFailedToScoreRate(a10, away);

            // === BTTS SPECIFIC (ROLLING) ===
            f.HomeBTTSFreq5 = CalculateBTTSRate(h5);
            f.HomeBTTSFreq10 = CalculateBTTSRate(h10);
            f.AwayBTTSFreq5 = CalculateBTTSRate(a5);
            f.AwayBTTSFreq10 = CalculateBTTSRate(a10);

            // === WIN/LOSS/DRAW RATES ===
            f.HomeWinRate5 = CalculateResultRate(h5, home, "W");
            f.HomeWinRate10 = CalculateResultRate(h10, home, "W");
            f.HomeLossRate5 = CalculateResultRate(h5, home, "L");
            f.HomeLossRate10 = CalculateResultRate(h10, home, "L");
            
            f.AwayWinRate5 = CalculateResultRate(a5, away, "W");
            f.AwayWinRate10 = CalculateResultRate(a10, away, "W");
            f.AwayLossRate5 = CalculateResultRate(a5, away, "L");
            f.AwayLossRate10 = CalculateResultRate(a10, away, "L");

            f.HomeDrawRate5 = CalculateDrawRate(h5);
            f.HomeDrawRate10 = CalculateDrawRate(h10);
            f.AwayDrawRate5 = CalculateDrawRate(a5);
            f.AwayDrawRate10 = CalculateDrawRate(a10);
            f.CombinedDrawRate10 = (f.HomeDrawRate10 + f.AwayDrawRate10) / 2.0f;

            // === HEAD-TO-HEAD ===
            // Get past matches between these two
            var h2h = homeHistory.Where(m => m.HomeTeam == away || m.AwayTeam == away).ToList(); 
            // Note: awayHistory contains the same matches where 'away' played 'home'.
            // Filtering homeHistory for 'away' is sufficient.
            
            f.H2HMatchesCount = h2h.Count;
            if (f.H2HMatchesCount > 0)
            {
                f.H2HAvgTotalGoals = (float)h2h.Average(m => m.FTHG + m.FTAG);
                f.H2HUnder25Rate = (float)h2h.Count(m => (m.FTHG + m.FTAG) < 2.5) / f.H2HMatchesCount;
                f.H2HZeroZeroRate = (float)h2h.Count(m => (m.FTHG + m.FTAG) == 0) / f.H2HMatchesCount;
                // Time Decay: More recent matches matter more? 
                // For simplicity: Inverse average days ago? Or just simple count weight.
                f.H2HTimeDecayWeight = (float)(1.0 / (1.0 + (date - h2h.Last().Date).TotalDays / 365.0));
            }

            // === MARKET-DERIVED ===
            if (match.Odds != null)
            {
                f.OddsOver15 = 0; // Not in standard DTO usually, assume derived or missing
                f.OddsOver25 = (float)match.Odds.Over25;
                f.OddsDraw = (float)match.Odds.Draw;
                f.OddsOver25ImpliedProb = f.OddsOver25 > 0 ? 1.0f / f.OddsOver25 : 0;
                f.OddsOver15ImpliedProb = 0; // Missing source
                f.BookmakerGoalExpectation = 0; // Complex derivation omitted
                
                // Trap Score: High prob but low expectation?
                // Example: If Odds < 1.5 but AvgGoals < 2.0 -> Trap?
            }

            // === ENGINEERED TRAP SIGNALS ===
            f.BothTeamsDefensive = (f.HomeGoalsAgainstAvg10 < 1.0f && f.AwayGoalsAgainstAvg10 < 1.0f);
            f.BothTeamsLowScoring = (f.HomeGoalsForAvg10 < 1.0f && f.AwayGoalsForAvg10 < 1.0f);
            f.BothTeamsPoorForm = (CalculatePointsAvg(h5, home) < 1.0f && CalculatePointsAvg(a5, away) < 1.0f);
            f.HighDrawBias = f.CombinedDrawRate10 > 0.35f;
            
            f.EuropeFatigueTrap = (f.HomePlayedEuropeLast7d || f.AwayPlayedEuropeLast7d) && f.LeagueId != 0; 
            // Logic: playing after Europe often leads to underperformance/rotation -> Low Scoring Trap?

            // === TARGET LABELS ===
            // This is the "Ground Truth" for Training.
            var totalGoals = match.FTHG + match.FTAG;
            f.LabelTotalGoals = totalGoals;
            f.LabelIsOver15 = totalGoals > 1.5;
            f.LabelIsOver25 = totalGoals > 2.5;
            f.LabelIsBTTS = match.FTHG > 0 && match.FTAG > 0;
            f.LabelIsDraw = match.FTHG == match.FTAG;
            f.LabelIsZeroZero = totalGoals == 0;
            
            // Trap Label: Unders when market expected Overs?
            // "Low Goal Trap" = Market expects Over 2.5 (Odds < 1.80?) BUT Result is Under 2.5
            // Or simple definition: Result is Under 2.5 despite heavy fav / over trends?
            // For now, let's define Trap as Under 2.5 match (since we predict Overs, Under is the "Trap" we want to avoid or flag).
            // Actually, usually "Trap" means "Looks like Over, Is Under". 
            // But if we just predict IsLowGoalTrap likelihood, we can subtract it from Over score. 
            // So LabelIsLowGoalTrap = (TotalGoals < 2.5).
            f.LabelIsLowGoalTrap = totalGoals < 2.5;


            features.Add(f);

            // 3. Update History
            teamHistory[home].Add(match);
            teamHistory[away].Add(match);
        }

        return features;
    }

    // --- Helpers ---

    private float CalculateAvgGoals(List<HistoricalMatchDto> matches, string team, bool isFor)
    {
        if (matches.Count == 0) return 0;
        int sum = 0;
        foreach (var m in matches)
        {
            if (m.HomeTeam == team) sum += isFor ? m.FTHG : m.FTAG;
            else sum += isFor ? m.FTAG : m.FTHG;
        }
        return (float)sum / matches.Count;
    }

    private float CalculateCleanSheetRate(List<HistoricalMatchDto> matches, string team)
    {
        if (matches.Count == 0) return 0;
        int cleanSheets = 0;
        foreach (var m in matches)
        {
            int goalsConceded = (m.HomeTeam == team) ? m.FTAG : m.FTHG;
            if (goalsConceded == 0) cleanSheets++;
        }
        return (float)cleanSheets / matches.Count;
    }

    private float CalculateFailedToScoreRate(List<HistoricalMatchDto> matches, string team)
    {
        if (matches.Count == 0) return 0;
        int failed = 0;
        foreach (var m in matches)
        {
            int goalsScored = (m.HomeTeam == team) ? m.FTHG : m.FTAG;
            if (goalsScored == 0) failed++;
        }
        return (float)failed / matches.Count;
    }

    private float CalculateBTTSRate(List<HistoricalMatchDto> matches)
    {
        if (matches.Count == 0) return 0;
        int btts = matches.Count(m => m.FTHG > 0 && m.FTAG > 0);
        return (float)btts / matches.Count;
    }

    private float CalculateDrawRate(List<HistoricalMatchDto> matches)
    {
        if (matches.Count == 0) return 0;
        int draws = matches.Count(m => m.FTHG == m.FTAG);
        return (float)draws / matches.Count;
    }

    private float CalculatePointsAvg(List<HistoricalMatchDto> matches, string team)
    {
        if (matches.Count == 0) return 0;
        int points = 0;
        foreach (var m in matches)
        {
            bool isHome = m.HomeTeam == team;
            int gFor = isHome ? m.FTHG : m.FTAG;
            int gAg = isHome ? m.FTAG : m.FTHG;
            
            if (gFor > gAg) points += 3;
            else if (gFor == gAg) points += 1;
        }
        return (float)points / matches.Count;
    }
    
    private float CalculateResultRate(List<HistoricalMatchDto> matches, string team, string resultType)
    {
        if (matches.Count == 0) return 0;
        int count = 0;
        foreach (var m in matches)
        {
            bool isHome = m.HomeTeam == team;
            // resultType: "W" = Win, "L" = Loss
            bool win = (isHome && m.FTR == "H") || (!isHome && m.FTR == "A");
            bool loss = (isHome && m.FTR == "A") || (!isHome && m.FTR == "H");
            
            if (resultType == "W" && win) count++;
            else if (resultType == "L" && loss) count++;
        }
        return (float)count / matches.Count;
    }
    
    private float HashLeague(string league)
    {
        if (string.IsNullOrEmpty(league)) return 0;
        return (float)Math.Abs(league.GetHashCode() % 1000); // Simple categorical ID
    }
}
