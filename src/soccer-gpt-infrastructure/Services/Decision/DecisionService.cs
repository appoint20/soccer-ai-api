using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services.Decision;

public class DecisionService : IDecisionService
{
    // Confidence Thresholds (Probability)
    private const double MIN_CONFIDENCE_OVER25 = 0.60;   // 60%
    private const double MIN_CONFIDENCE_BTTS = 0.58;     // 58%
    private const double MIN_CONFIDENCE_HOME = 0.65;     // 65%
    private const double MIN_CONFIDENCE_AWAY = 0.62;     // 62%
    private const double MIN_CONFIDENCE_2TO3 = 0.55;     // 55%

    // Minimum Odds Requirements
    private const double MIN_ODDS_OVER25 = 1.80;
    private const double MIN_ODDS_BTTS = 1.85;
    private const double MIN_ODDS_HOME = 2.00;
    private const double MIN_ODDS_AWAY = 1.90;

    // H2H Boost
    private const double H2H_BOOST = 0.05;  // +5% confidence if H2H supports

    public BettingDecisionDto MakeDecision(
        MatchProbabilitiesDto probabilities, 
        H2HAnalysisDto h2hAnalysis,
        MatchOddsDto? odds = null)
    {
        var decision = new BettingDecisionDto();
        
        // Candidate markets with boosted probabilities
        var candidates = new List<(string Market, double Probability, double MinConfidence, double MinOdds, bool HasH2H)>();

        // 1. Over 2.5 (Best accuracy: 54.49%)
        double over25Prob = probabilities.Over25;
        bool over25H2H = h2hAnalysis.IsOver25Candidate;
        if (over25H2H) over25Prob = Math.Min(over25Prob + H2H_BOOST, 0.95);
        
        candidates.Add(("Over 2.5 Goals", over25Prob, MIN_CONFIDENCE_OVER25, MIN_ODDS_OVER25, over25H2H));

        // 2. 2-3 Goals (Good H2H coverage: 26.4%)
        double twoToThreeProb = probabilities.Prob2to3Goals;
        bool twoToThreeH2H = h2hAnalysis.Is2to3GoalsCandidate;
        if (twoToThreeH2H) twoToThreeProb = Math.Min(twoToThreeProb + H2H_BOOST, 0.95);
        
        candidates.Add(("2-3 Goals", twoToThreeProb, MIN_CONFIDENCE_2TO3, 2.00, twoToThreeH2H));

        // 3. BTTS (Needs H2H support)
        double bttsProb = probabilities.BTTS;
        bool bttsH2H = h2hAnalysis.IsBTTSCandidate;
        if (bttsH2H) bttsProb = Math.Min(bttsProb + H2H_BOOST, 0.95);
        
        candidates.Add(("BTTS Yes", bttsProb, MIN_CONFIDENCE_BTTS, MIN_ODDS_BTTS, bttsH2H));

        // 4. Away Win
        double awayProb = probabilities.AwayWin;
        bool awayH2H = h2hAnalysis.IsAwayWinCandidate;
        if (awayH2H) awayProb = Math.Min(awayProb + H2H_BOOST, 0.95);
        
        candidates.Add(("Away Win", awayProb, MIN_CONFIDENCE_AWAY, MIN_ODDS_AWAY, awayH2H));

        // 5. Home Win (Highest threshold due to favorite trap)
        double homeProb = probabilities.HomeWin;
        bool homeH2H = h2hAnalysis.IsHomeWinCandidate;
        if (homeH2H) homeProb = Math.Min(homeProb + H2H_BOOST, 0.95);
        
        candidates.Add(("Home Win", homeProb, MIN_CONFIDENCE_HOME, MIN_ODDS_HOME, homeH2H));

        // Evaluate candidates and select best
        double bestEV = -1;
        
        foreach (var (market, prob, minConf, minOdds, hasH2H) in candidates)
        {
            // Check confidence threshold
            if (prob < minConf) continue;

            // Check odds requirement (if odds available)
            double marketOdds = GetMarketOdds(market, odds);
            if (marketOdds > 0 && marketOdds < minOdds) continue;

            // Calculate expected value
            double ev = marketOdds > 0 ? (prob * marketOdds) - 1 : 0;

            if (ev > bestEV)
            {
                bestEV = ev;
                decision.SelectedMarket = market;
                decision.Confidence = prob;
                decision.ExpectedValue = ev;
                decision.IsHighConfidence = true;
                decision.HasH2HSupport = hasH2H;
                decision.Reasons.Clear();
                
                decision.Reasons.Add($"{market} selected with {prob:P1} confidence");
                if (hasH2H) decision.Reasons.Add($"H2H Support: +{H2H_BOOST:P0} boost applied");
                if (marketOdds > 0) decision.Reasons.Add($"Odds: {marketOdds:F2}, EV: {ev:P1}");
                decision.Reasons.Add($"Threshold: {minConf:P0} (passed)");
            }
        }

        // If no market passed thresholds
        if (!decision.IsHighConfidence)
        {
            decision.SelectedMarket = "No Bet";
            decision.Confidence = candidates.Max(c => c.Probability);
            decision.Reasons.Add("No market passed confidence/odds thresholds");
        }

        return decision;
    }

    private double GetMarketOdds(string market, MatchOddsDto? odds)
    {
        if (odds == null) return 0;

        return market switch
        {
            "Over 2.5 Goals" => (double)odds.Over25,
            "BTTS Yes" => (double)odds.BttsYes,
            "Home Win" => (double)odds.HomeWin,
            "Away Win" => (double)odds.AwayWin,
            "2-3 Goals" => 2.00, // Default odds for 2-3 goals
            _ => 0
        };
    }
}
