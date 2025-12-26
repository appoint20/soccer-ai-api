using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Decision matrix for Over 2.5 Goals market
/// Determines which leagues are suitable for ticket generation vs upcoming games display
/// </summary>
public interface IOver25DecisionMatrix
{
    /// <summary>
    /// Determine if Over 2.5 bet should be included in ticket generation
    /// Excludes problematic leagues: D1, F1, SP1 and risky odds (<1.76)
    /// </summary>
    bool AllowInTicket(string league, double confidence, double odds = 0);
    
    /// <summary>
    /// Determine if Over 2.5 bet should be shown in upcoming games
    /// Shows all leagues with appropriate confidence threshold
    /// </summary>
    bool AllowInUpcomingGames(string league, double confidence);
    
    /// <summary>
    /// Get the reason why a bet was excluded from tickets
    /// </summary>
    string GetTicketExclusionReason(string league);
}
