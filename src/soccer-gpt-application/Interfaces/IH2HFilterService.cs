using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Interfaces;

public interface IH2HFilterService
{
    H2HAnalysisDto AnalyzeH2H(string homeTeam, string awayTeam, List<HistoricalMatchDto> allHistory);
}

public class H2HAnalysisDto
{
    public bool IsDerby { get; set; }
    public int H2HMatchesCount { get; set; }
    
    // Last 5 H2H Patterns (Strict: All 5 matches)
    public bool IsBTTSCandidate { get; set; }           // All 5 had BTTS
    public bool IsOver25Candidate { get; set; }         // All 5 had Over 2.5
    public bool Is2to3GoalsCandidate { get; set; }      // 4+ of 5 had 2-3 goals
    public bool IsHomeWinCandidate { get; set; }        // Home team won 4+ of 5
    public bool IsAwayWinCandidate { get; set; }        // Away team won 4+ of 5
    public bool IsDrawCandidate { get; set; }           // 3+ draws in 5
    
    // Summary Stats (for last 5 H2H)
    public int BTTSCount { get; set; }
    public int Over25Count { get; set; }
    public int TwoToThreeGoalsCount { get; set; }
    public int HomeWins { get; set; }
    public int AwayWins { get; set; }
    public int Draws { get; set; }
    
    public List<string> Tags { get; set; } = new();
}
