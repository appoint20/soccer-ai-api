using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class RecentFormService : IRecentFormService
{
    private readonly ILogger<RecentFormService> _logger;
    
    public RecentFormService(ILogger<RecentFormService> logger)
    {
        _logger = logger;
    }
    
    public FormStats CalculateRecentForm(string team, List<HistoricalMatchDto> history, bool isHome)
    {
        var recentMatches = history
            .Where(m => isHome 
                ? IsMatch(m.HomeTeam, team)
                : IsMatch(m.AwayTeam, team))
            .OrderByDescending(m => m.Date)
            .Take(10)
            .ToList();
            
        if (recentMatches.Count < 5)
        {
            _logger.LogDebug("Insufficient form data for {Team} ({IsHome}): only {Count} matches",
                team, isHome ? "Home" : "Away", recentMatches.Count);
            return FormStats.Default();
        }
            
        double cleanSheets = recentMatches.Count(m => 
            isHome ? m.FTAG == 0 : m.FTHG == 0);
            
        double failedToScore = recentMatches.Count(m => 
            isHome ? m.FTHG == 0 : m.FTAG == 0);
            
        return new FormStats
        {
            CleanSheetRate = cleanSheets / recentMatches.Count,
            FailedToScoreRate = failedToScore / recentMatches.Count,
            MatchesAnalyzed = recentMatches.Count
        };
    }
    
    private bool IsMatch(string team1, string team2)
    {
        if (string.IsNullOrWhiteSpace(team1) || string.IsNullOrWhiteSpace(team2)) return false;
        return string.Equals(team1.Trim(), team2.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
