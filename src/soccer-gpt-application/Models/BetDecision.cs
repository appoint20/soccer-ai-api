namespace soccer_gpt_application.Models;

/// <summary>
/// Result of defensive filter evaluation - either allows or blocks a bet
/// </summary>
public class BetDecision
{
    public string Market { get; set; } = "";
    public bool IsBetAllowed { get; set; }
    public string BlockReason { get; set; } = "";
    
    public static BetDecision Allow(string market)
    {
        return new BetDecision
        {
            Market = market,
            IsBetAllowed = true,
            BlockReason = ""
        };
    }
    
    public static BetDecision Block(string market, string reason)
    {
        return new BetDecision
        {
            Market = market,
            IsBetAllowed = false,
            BlockReason = reason
        };
    }
}
