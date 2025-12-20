using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Traps;

public interface ITrapDetector
{
    string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics);
}

public class TrapDetectionService(ILogger<TrapDetectionService> logger, IEnumerable<ITrapDetector> detectors)
    : ITrapDetectionService
{
    public List<string> AnalyzeTraps(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        var traps = new List<string>();
        foreach (var detector in detectors)
        {
            try
            {
                var result = detector.DetectTrap(match, analytics);
                if (!string.IsNullOrEmpty(result))
                {
                    traps.Add(result);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error running trap detector {Detector}", detector.GetType().Name);
            }
        }
        return traps;
    }
}

// --- Strategies ---

public class BoreDrawDetector : ITrapDetector
{
    public string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        // Use Data-Driven Thresholds (e.g. 50% implies bias towards Under)
        // Ideally this threshold comes from config. For now, we use a standard statistical significant value (0.60 for Under -> < 0.40 for Over)
        if (analytics.Probabilities.Over15 < 0.40) 
        {
             return $"Bore Draw Risk: Model indicates only {analytics.Probabilities.Over15:P0} chance of Over 1.5 Goals.";
        }
        return null;
    }
}

public class OddsTrapDetector : ITrapDetector
{
    public string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        if (match.Odds == null) return null;

        // Logic: Compare Implied Probability vs Model Probability
        // Real logic: Market Price (Odds) vs Our Price (Model)
        // If discrepancy is large for a Favorite, it's a Trap.
        
        decimal homeImplied = 1.0m / (match.Odds.HomeWin == 0 ? 1 : match.Odds.HomeWin);
        double modelHomeWin = analytics.Probabilities.HomeWin;

        // If Market implies 66% (1.50) but Model says 40%, that is a massive discrepancy.
        // "Trap" usually means the bookie PRICE is enticing (Low Odds = High Prob) but reality is different.
        
        if (match.Odds.HomeWin < 1.50m && modelHomeWin < 0.50)
        {
             return $"Odds Trap: Market expects Home Win ({homeImplied:P0}) but Model disagrees ({modelHomeWin:P0}).";
        }
        
        // Check Status based reversion
        if (match.Odds.HomeWin < 1.60m && analytics.StreakAnalysis.Status.Contains("Overperforming"))
        {
             return $"Form Trap: {match.HomeTeam} is favorite but marked as Overperforming (Reversion Risk).";
        }

        return null;
    }
}

public class EuropeanFatigueDetector : ITrapDetector
{
    private readonly IEuropeanFixturesService _fixturesService;
    private readonly ILogger<EuropeanFatigueDetector> _logger;
    
    // Robust checking against normalized league names
    private static readonly HashSet<string> EuroKeywords = new(StringComparer.OrdinalIgnoreCase) 
    { 
        "Champions League", "Europa", "Conference", "UCL", "UEL" 
    };

    public EuropeanFatigueDetector(
        IEuropeanFixturesService fixturesService,
        ILogger<EuropeanFatigueDetector> logger)
    {
        _fixturesService = fixturesService;
        _logger = logger;
    }

    public string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        // 1. Context Check (Is THIS a Euro game?)
        if (!string.IsNullOrEmpty(match.LeagueName) && EuroKeywords.Any(k => match.LeagueName.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
             return "European Night: High intensity match.";
        }
        
        // 2. Real Schedule Analysis (Fatigue & Distraction)
        try
        {
            // Check both teams for recent European fixtures
            var homeFatigue = CheckEuropeanFatigueAsync(match.HomeTeam, match.Date).GetAwaiter().GetResult();
            var awayFatigue = CheckEuropeanFatigueAsync(match.AwayTeam, match.Date).GetAwaiter().GetResult();
            
            // Both teams have European fatigue
            if (homeFatigue != null && awayFatigue != null)
            {
                return $"Double European Fatigue: Both teams played in Europe recently. {homeFatigue} {awayFatigue}";
            }
            
            // Home team European fatigue
            if (homeFatigue != null)
            {
                return $"Home Team Fatigue: {homeFatigue}";
            }
            
            // Away team European fatigue
            if (awayFatigue != null)
            {
                return $"Away Team Fatigue: {awayFatigue}";
            }
            
            // Check for upcoming distractions
            var homeDistraction = CheckUpcomingDistractionAsync(match.HomeTeam).GetAwaiter().GetResult();
            var awayDistraction = CheckUpcomingDistractionAsync(match.AwayTeam).GetAwaiter().GetResult();
            
            if (homeDistraction != null || awayDistraction != null)
            {
                var distractions = new List<string>();
                if (homeDistraction != null) distractions.Add($"{match.HomeTeam}: {homeDistraction}");
                if (awayDistraction != null) distractions.Add($"{match.AwayTeam}: {awayDistraction}");
                
                return $"European Distraction: {string.Join("; ", distractions)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking European fixtures for {HomeTeam} vs {AwayTeam}", 
                match.HomeTeam, match.AwayTeam);
        }

        return null;
    }
    
    private async Task<string?> CheckEuropeanFatigueAsync(string teamName, string matchDateStr)
    {
        // Parse match date
        if (!DateTime.TryParse(matchDateStr, out var matchDate))
            return null;
        
        // Get team fixtures
        var teamFixtures = await _fixturesService.GetTeamFixturesAsync(teamName);
        if (teamFixtures is not { HasRecentEuropean: true })
            return null;
        
        // Find most recent European match
        var recentMatch = teamFixtures.RecentMatches
            .Where(m => m.DateParsed.HasValue && m.DateParsed.Value < matchDate)
            .MaxBy(m => m.DateParsed);
        
        if (recentMatch == null)
            return null;
        
        var daysSinceEuropean = (matchDate - recentMatch.DateParsed!.Value).TotalDays;
        
        // Critical fatigue (0-3 days)
        if (daysSinceEuropean <= 3)
        {
            var opponent = recentMatch.Venue == "home" ? recentMatch.AwayTeam : recentMatch.HomeTeam;
            return $"CRITICAL: Played {recentMatch.Competition} vs {opponent} {daysSinceEuropean:F0} days ago";
        }
        
        // Moderate fatigue (4-7 days)
        if (daysSinceEuropean <= 7)
        {
            var opponent = recentMatch.Venue == "home" ? recentMatch.AwayTeam : recentMatch.HomeTeam;
            return $"Moderate: {recentMatch.Competition} vs {opponent} {daysSinceEuropean:F0} days ago";
        }
        
        return null;
    }
    
    private async Task<string?> CheckUpcomingDistractionAsync(string teamName)
    {
        var teamFixtures = await _fixturesService.GetTeamFixturesAsync(teamName);
        if (teamFixtures is not { HasUpcomingEuropean: true })
            return null;
        
        // Find next European match
        var nextMatch = teamFixtures.UpcomingMatches
            .Where(m => m.DateParsed.HasValue && m.DateParsed.Value > DateTime.UtcNow)
            .MinBy(m => m.DateParsed);
        
        if (nextMatch == null)
            return null;
        
        var daysUntilEuropean = (nextMatch.DateParsed!.Value - DateTime.UtcNow).TotalDays;
        
        // Distraction if European match within 3-5 days after
        if (daysUntilEuropean >= 3 && daysUntilEuropean <= 5)
        {
            var opponent = nextMatch.Venue == "home" ? nextMatch.AwayTeam : nextMatch.HomeTeam;
            return $"Big {nextMatch.Competition} match vs {opponent} in {daysUntilEuropean:F0} days";
        }
        
        return null;
    }
    
    private bool IsEuropean(string comp)
    {
        return EuroKeywords.Any(k => comp.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

public class DerbyDetector : ITrapDetector
{
    private readonly ILogger<DerbyDetector> _logger;
    private readonly Dictionary<string, List<DerbyRivalry>> _derbies;
    
    public DerbyDetector(ILogger<DerbyDetector> logger)
    {
        _logger = logger;
        _derbies = LoadDerbyRivalries();
    }
    
    public string? DetectTrap(UpcomingMatchDto match, AdvancedAnalyticsDto analytics)
    {
        string homeTeam = match.HomeTeam;
        string awayTeam = match.AwayTeam;
        
        // Check all loaded derbies
        foreach (var (leagueKey, rivalries) in _derbies)
        {
            foreach (var derby in rivalries)
            {
                if (IsRivalryMatch(homeTeam, awayTeam, derby.Teams))
                {
                    var intensityIndicator = derby.Intensity switch
                    {
                        "very-high" => "⚠️ INTENSE",
                        "high" => "⚡",
                        _ => ""
                    };
                    
                    return $"Derby Match {intensityIndicator}: {derby.Name} - {derby.Description}";
                }
            }
        }
        
        return null;
    }
    
    private bool IsRivalryMatch(string homeTeam, string awayTeam, List<string> rivalryTeams)
    {
        if (rivalryTeams.Count != 2) return false;
        
        var team1 = rivalryTeams[0];
        var team2 = rivalryTeams[1];
        
        // Check if home and away teams match the rivalry (in any order)
        return (TeamContains(homeTeam, team1) && TeamContains(awayTeam, team2)) ||
               (TeamContains(homeTeam, team2) && TeamContains(awayTeam, team1));
    }
    
    private bool TeamContains(string fullTeamName, string rivalryTeamName)
    {
        return fullTeamName.Contains(rivalryTeamName, StringComparison.OrdinalIgnoreCase);
    }
    
    private Dictionary<string, List<DerbyRivalry>> LoadDerbyRivalries()
    {
        try
        {
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "derby_rivalries.json");
            
            if (!File.Exists(dataPath))
            {
                _logger.LogWarning("Derby rivalries file not found at {Path}", dataPath);
                return new Dictionary<string, List<DerbyRivalry>>();
            }
            
            var json = File.ReadAllText(dataPath);
            var data = System.Text.Json.JsonSerializer.Deserialize<DerbyRivalriesData>(json);
            
            if (data?.Leagues == null)
            {
                _logger.LogWarning("Failed to deserialize derby rivalries data");
                return new Dictionary<string, List<DerbyRivalry>>();
            }
            
            var derbies = new Dictionary<string, List<DerbyRivalry>>();
            
            // Flatten all league rivalries into a single dictionary
            foreach (var (leagueKey, league) in data.Leagues)
            {
                if (league.Rivalries != null && league.Rivalries.Count > 0)
                {
                    derbies[leagueKey] = league.Rivalries;
                }
            }
            
            var totalDerbies = derbies.Values.Sum(list => list.Count);
            _logger.LogInformation("Loaded {Count} derby rivalries across {LeagueCount} leagues", 
                totalDerbies, derbies.Count);
            
            return derbies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading derby rivalries from JSON");
            return new Dictionary<string, List<DerbyRivalry>>();
        }
    }
}

// JSON Models for Derby Rivalries
public record DerbyRivalriesData
{
    public string Version { get; init; } = string.Empty;
    public string LastUpdated { get; init; } = string.Empty;
    public Dictionary<string, LeagueRivalries> Leagues { get; init; } = new();
}

public record LeagueRivalries
{
    public int LeagueId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public List<DerbyRivalry> Rivalries { get; init; } = new();
}

public record DerbyRivalry
{
    public string Name { get; init; } = string.Empty;
    public List<string> Teams { get; init; } = new();
    public string Intensity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
