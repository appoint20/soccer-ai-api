using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class EuropeanFatigueService : IEuropeanFatigueService
{
    private readonly ILogger<EuropeanFatigueService> _logger;
    
    // European competition league codes
    private static readonly HashSet<string> EuropeanCompetitions = new()
    {
        "EC",   // Champions League
        "UCL",  // Champions League (alternative)
        "UEL",  // Europa League
        "UECL"  // Conference League
    };

    public EuropeanFatigueService(ILogger<EuropeanFatigueService> logger)
    {
        _logger = logger;
    }

    public bool HasRecentEuropeanFixture(
        string teamName, 
        DateTime matchDate, 
        List<HistoricalMatchDto> history, 
        int lookbackDays = 7)
    {
        if (string.IsNullOrWhiteSpace(teamName) || history == null || !history.Any())
        {
            return false;
        }

        // Calculate the lookback window
        var startDate = matchDate.AddDays(-lookbackDays);

        // Search for European matches involving this team within the lookback period
        var recentEuropeanMatches = history
            .Where(m => 
                // Match is within lookback window
                m.Date >= startDate && m.Date < matchDate &&
                // Match is a European competition
                !string.IsNullOrEmpty(m.League) && EuropeanCompetitions.Contains(m.League) &&
                // Team is involved (home or away)
                (m.HomeTeam.Equals(teamName, StringComparison.OrdinalIgnoreCase) || 
                 m.AwayTeam.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (recentEuropeanMatches.Any())
        {
            _logger.LogInformation(
                "European fixture congestion detected for {TeamName}. Found {Count} European match(es) in last {Days} days before {MatchDate:yyyy-MM-dd}",
                teamName, recentEuropeanMatches.Count, lookbackDays, matchDate);
            
            return true;
        }

        return false;
    }
}
