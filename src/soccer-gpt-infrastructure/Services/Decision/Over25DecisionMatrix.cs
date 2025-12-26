using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services.Decision;

/// <summary>
/// Decision matrix for Over 2.5 Goals market
/// Excludes high-failure leagues (D1, F1, F2, SP1) from ticket generation
/// Based on empirical analysis: these 4 leagues contribute 65.5% of all Over 2.5 failures
/// </summary>
public class Over25DecisionMatrix : IOver25DecisionMatrix
{
    private readonly ILogger<Over25DecisionMatrix> _logger;
    
    // Leagues to exclude from ticket generation due to high failure rates or low win rates
    // D1 (Bundesliga): 13 failures (22.4% of all failures), 59.4% win rate
    // F2 (Ligue 2): 3 failures, 40.0% win rate in tickets (2/5)
    // SP1 (La Liga): 12 failures (20.7% of all failures), 62.5% win rate
    // I2 (Serie B): 1 failure, 0% win rate (0/1) + only 1 qualified due to 63% threshold
    // F1 (Ligue 1): KEPT - user requested to include
    private static readonly HashSet<string> TicketExcludedLeagues = new()
    {
        "D1",  // Bundesliga
        "F2",  // Ligue 2
        "SP1", // La Liga
        "I2"   // Serie B
    };

    
    // Minimum odds for ticket generation
    // Odds below 1.76 are considered "risky" - low reward for the risk
    private const double MinTicketOdds = 1.76;
    
    // Italian leagues require higher confidence (63%)
    private static readonly HashSet<string> ItalianLeagues = new()
    {
        "I1",  // Serie A
        "I2"   // Serie B
    };
    
    private const double MinConfidenceDefault = 0.50;   // 50% for most leagues
    private const double MinConfidenceItalian = 0.63;   // 63% for Italian leagues
    
    public Over25DecisionMatrix(ILogger<Over25DecisionMatrix> logger)
    {
        _logger = logger;
    }
    
    public bool AllowInTicket(string league, double confidence, double odds = 0)
    {
        // Never allow excluded leagues in tickets
        if (TicketExcludedLeagues.Contains(league))
        {
            _logger.LogDebug("Over 2.5 excluded from ticket: {League} (high failure rate league)", league);
            return false;
        }
        
        // Exclude risky low-odds bets from tickets
        if (odds > 0 && odds < MinTicketOdds)
        {
            _logger.LogDebug("Over 2.5 excluded from ticket: odds {Odds:F2} below minimum {Min:F2} (risky/low reward)", 
                odds, MinTicketOdds);
            return false;
        }
        
        // Apply confidence threshold
        var minConfidence = GetMinConfidence(league);
        var allowed = confidence >= minConfidence;
        
        if (!allowed)
        {
            _logger.LogDebug("Over 2.5 excluded from ticket: {League} confidence {Confidence:P0} below {Min:P0}", 
                league, confidence, minConfidence);
        }
        
        return allowed;
    }
    
    public bool AllowInUpcomingGames(string league, double confidence)
    {
        // Upcoming games can show all leagues
        // Only apply confidence threshold
        var minConfidence = GetMinConfidence(league);
        return confidence >= minConfidence;
    }
    
    public string GetTicketExclusionReason(string league)
    {
        if (TicketExcludedLeagues.Contains(league))
        {
            return league switch
            {
                "D1" => "Bundesliga excluded: 22.4% of Over 2.5 failures, 2-goal trap prevalent",
                "F1" => "Ligue 1 excluded: 17.2% of Over 2.5 failures, inconsistent scoring",
                "F2" => "Ligue 2 excluded: 40% win rate in tickets, low quality",
                "SP1" => "La Liga excluded: 20.7% of Over 2.5 failures, favorites stop at 2-0",
                "I2" => "Serie B excluded: 0% win rate (0/1), very restrictive 63% threshold",
                _ => $"{league} excluded from tickets"
            };
        }
        
        return string.Empty;
    }
    
    private double GetMinConfidence(string league)
    {
        // Italian leagues need higher confidence
        if (ItalianLeagues.Contains(league))
        {
            return MinConfidenceItalian;
        }
        
        // Default confidence threshold
        return MinConfidenceDefault;
    }
}
