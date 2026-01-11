using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using System.Text.RegularExpressions;

namespace soccer_gpt_infrastructure.Services;

public class TeamAnalyticsService : ITeamAnalyticsService
{
    public TeamPerformanceStats CalculateStats(List<HistoricalMatchDto> matches, string teamName)
    {
        if (matches == null || matches.Count == 0)
        {
            return new TeamPerformanceStats();
        }

        int played = matches.Count;
        int scored = 0;
        int conceded = 0;
        int over25Count = 0;
        int bttsCount = 0;
        int goals23Count = 0;

        foreach (var m in matches)
        {
            bool isHome = IsTeam(m.HomeTeam, teamName);
            // If teamName not found (shouldn't happen if filtered correctly), assume Home or skip?
            // Safer to check. 
            if (!isHome && !IsTeam(m.AwayTeam, teamName))
            {
                // Logic error in caller? Or mismatched name.
                // Logically we skip or throw. Let's skip to be safe.
                played--; 
                continue;
            }

            // Stats
            int gf = isHome ? m.FTHG : m.FTAG;
            int ga = isHome ? m.FTAG : m.FTHG;

            scored += gf;
            conceded += ga;

            if (m.IsOver25) over25Count++;
            if (m.IsBtts) bttsCount++;
            if (m.Is2to3Goals) goals23Count++;
        }

        if (played == 0) return new TeamPerformanceStats();

        return new TeamPerformanceStats
        {
            MatchesPlayed = played,
            GoalsScored = scored,
            GoalsConceded = conceded,
            GoalsScoredAvg = Math.Round((double)scored / played, 2),
            GoalsConcededAvg = Math.Round((double)conceded / played, 2),
            Over25Percentage = Math.Round((double)over25Count / played, 2),
            BTTSPercentage = Math.Round((double)bttsCount / played, 2),
            Goals2To3Percentage = Math.Round((double)goals23Count / played, 2)
        };
    }

    private bool IsTeam(string s1, string s2)
    {
        return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);
    }
}
