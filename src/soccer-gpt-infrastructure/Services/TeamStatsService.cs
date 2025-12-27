using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class TeamStatsService : ITeamStatsService
{
    private const int StatSampleBox = 10; // Use last 10 games for general stats

    public Task<RichTeamStatsDto> CalculateStatsAsync(string teamName, List<HistoricalMatchDto> history)
    {
        // 1. Filter matches where team played (sanity check)
        // History is usually passed pre-filtered for H2H, but for Team Stats we need THE TEAM'S history vs ANYONE.
        // The calling code (Controller) must ensure it passes the *Full* history of the team, not just H2H.
        // Assuming 'history' is chronological (Old -> New) or New -> Old. We'll sort to be safe.
        
        var recentMatches = history
            .Where(m => MatchIsRelevant(m, teamName))
            .OrderByDescending(m => m.Date) // Newest first
            .Take(StatSampleBox)
            .ToList();

        if (recentMatches.Count == 0)
        {
            return Task.FromResult(new RichTeamStatsDto { TeamName = teamName });
        }

        double totalGF = 0;
        double totalGA = 0;
        int bttsCount = 0;
        int over25Count = 0;
        int cleanSheets = 0;
        int failedToScore = 0;
        int wins = 0;

        foreach (var m in recentMatches)
        {
            bool isHome = IsMatch(m.HomeTeam, teamName);
            int gf = isHome ? m.FTHG : m.FTAG;
            int ga = isHome ? m.FTAG : m.FTHG;
            
            // Accumulate
            totalGF += gf;
            totalGA += ga;
            
            if (gf > 0 && ga > 0) bttsCount++;
            if ((gf + ga) > 2.5) over25Count++;
            if (ga == 0) cleanSheets++;
            if (gf == 0) failedToScore++;
            
            // Win check
            if (isHome && m.FTR == "H") wins++;
            else if (!isHome && m.FTR == "A") wins++;
        }

        double count = recentMatches.Count;

        var stats = new RichTeamStatsDto
        {
            TeamName = teamName,
            AvgGoalsFor = totalGF / count,
            AvgGoalsAgainst = totalGA / count,
            
            WinRateLast10 = (double)wins / count,
            BTTSPercentage = (double)bttsCount / count,
            Over25Percentage = (double)over25Count / count,
            CleanSheetPercentage = (double)cleanSheets / count,
            FailedToScorePercentage = (double)failedToScore / count,
            FormLast5 = GetFormString(recentMatches.Take(5).ToList(), teamName)
        };

        return Task.FromResult(stats);
    }

    private string GetFormString(List<HistoricalMatchDto> matches, string team)
    {
        // Matches are Newest -> Oldest
        // Form string usually Left=Recent
        var form = "";
        foreach (var m in matches)
        {
            if (m.FTR == "D")
            {
                form += "D";
                continue;
            }
            bool isHome = IsMatch(m.HomeTeam, team);
            if (isHome) form += (m.FTR == "H") ? "W" : "L";
            else form += (m.FTR == "A") ? "W" : "L";
        }
        return form;
    }

    private bool MatchIsRelevant(HistoricalMatchDto m, string team)
    {
        return IsMatch(m.HomeTeam, team) || IsMatch(m.AwayTeam, team);
    }

    private bool IsMatch(string s1, string s2)
    {
         if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;
        // Basic check, relying on Repository for deeper alias logic if needed, but local check is good
        if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return true;
        return s1.Contains(s2, StringComparison.OrdinalIgnoreCase) || s2.Contains(s1, StringComparison.OrdinalIgnoreCase);
    }
}
