using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.Decision;

public class DecisionService : IDecisionService
{
    private readonly IH2HReliabilityService _h2hService;
    private readonly IEuropeanFatigueService _europeanFatigueService;
    private readonly IRecentFormService _recentFormService;
    private readonly IPoissonFailureFilters _failureFilters;
    private readonly ILogger<DecisionService> _logger;
    
    // Confidence thresholds (Over 2.5 lowered to increase volume)
    private const double MinConfidenceOver25 = 0.50;  // Lowered to 50% for more Over 2.5 bets
    private const double MinConfidenceBtts = 0.55;    // Sweet spot
    private const double MinConfidence2to3 = 0.60;    // Sweet spot
    
    // Minimum odds to ensure value
    private const double MinOddsOver25 = 1.80;
    private const double MinOddsBtts = 1.85;
    
    // Excluded leagues (complete exclusion)
    private static readonly HashSet<string> ExcludedLeagues = new()
    {
        "D2",  // Bundesliga 2
        "P1",  // Primeira Liga
        "N1",  // Eredivisie
        "SP2"  // La Liga 2
    };
    
    // European competitions (for fixture congestion detection)
    private static readonly HashSet<string> EuropeanCompetitions = new()
    {
        "EC",   // Champions League
        "UEL",  // Europa League
        "UECL", // Conference League
        "UCL"   // Alternative Champions League code
    };
    
    // European fixture congestion penalty
    private const double EUROPEAN_FATIGUE_PENALTY = 0.90; // 10% confidence penalty for teams with European fixtures
    
    // Market Prioritization - BTTS bonus based on +17.4% historical ROI
    private const double BTTS_EV_BONUS = 0.05; // 5% bonus to EV for prioritization
    
    // League Filtering - Based on backtest showing E2 (-22% ROI), D2 (-10% ROI)
    // This is now replaced by ExcludedLeagues for complete exclusion
    // private const double WEAK_LEAGUE_PENALTY = 1.10; // Require 10% higher confidence
    
    // Model Consensus Requirement for Over 2.5
    private const double ML_DISAGREEMENT_PENALTY = 0.85; // 15% penalty if ML disagrees
    private const double ML_CONSENSUS_THRESHOLD = 0.60;  // ML must be >60% to avoid penalty

    public DecisionService(
        IH2HReliabilityService h2hService,
        IEuropeanFatigueService europeanFatigueService,
        IRecentFormService recentFormService,
        IPoissonFailureFilters failureFilters,
        ILogger<DecisionService> logger)
    {
        _h2hService = h2hService;
        _europeanFatigueService = europeanFatigueService;
        _recentFormService = recentFormService;
        _failureFilters = failureFilters;
        _logger = logger;
    }

    public BettingDecisionDto MakeDecision(
        string homeTeam,
        string awayTeam,
        MatchProbabilitiesDto probabilities,
        List<HistoricalMatchDto> history,
        string? league = null,
        MatchOddsDto? odds = null)
    {
        var decision = new BettingDecisionDto();
        
        // Early exit for excluded leagues
        if (!string.IsNullOrEmpty(league) && ExcludedLeagues.Contains(league))
        {
            _logger.LogInformation("Skipping {Home} vs {Away} - League {League} is excluded", 
                homeTeam, awayTeam, league);
            decision.Reasons.Add($"League {league} is excluded from predictions");
            return decision;
        }
        
        // Log input data
        _logger.LogInformation("Making decision for {Home} vs {Away}", homeTeam, awayTeam);
        _logger.LogInformation("League: {League}", league ?? "Unknown");

        // Build match context for defensive failure filters
        var matchContext = BuildMatchContext(homeTeam, awayTeam, probabilities, history, league, odds);

        var candidates = new List<(string Market, double RawProb, double FinalProb, double MinConfidence, double MinOdds, H2HResult H2H)>();

        // 1. Over 2.5 Goals
        var over25H2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "Over 2.5 Goals");
        // H2H is now informational only - no rejection
        double over25Final = probabilities.Over25 * over25H2H.Multiplier;
        
        // ML Consensus check DISABLED - user preference
        // if (mlPrediction != null && mlPrediction.Over25Probability < ML_CONSENSUS_THRESHOLD)
        // {
        //     over25Final *= ML_DISAGREEMENT_PENALTY; // 15% penalty if ML disagrees
        // }
        
        // European fatigue detection - apply penalty if either team has recent European fixtures
        var matchDate = DateTime.UtcNow; // For historical testing, this should be the match date
        bool homeHasEuropeanFatigue = _europeanFatigueService.HasRecentEuropeanFixture(homeTeam, matchDate, history);
        bool awayHasEuropeanFatigue = _europeanFatigueService.HasRecentEuropeanFixture(awayTeam, matchDate, history);
        
        if (homeHasEuropeanFatigue || awayHasEuropeanFatigue)
        {
            over25Final *= EUROPEAN_FATIGUE_PENALTY; // 10% penalty for rotation/fatigue risk
            var affectedTeams = new List<string>();
            if (homeHasEuropeanFatigue) affectedTeams.Add(homeTeam);
            if (awayHasEuropeanFatigue) affectedTeams.Add(awayTeam);
            _logger.LogInformation("European fatigue penalty applied for {Teams}", string.Join(" and ", affectedTeams));
        }
        
        // Apply defensive failure filters for Over 2.5
        var over25FilterResult = _failureFilters.ApplyOver25Filters(matchContext);
        if (over25FilterResult.IsAllowed)
        {
            // Use filtered confidence instead of direct calculation
            over25Final = over25FilterResult.FinalConfidence;
            if (over25FilterResult.Reasons.Any())
            {
                _logger.LogInformation("Over 2.5 filter applied: {Reasons}", string.Join(" | ", over25FilterResult.Reasons));
            }
            candidates.Add(("Over 2.5 Goals", probabilities.Over25, over25Final, MinConfidenceOver25, MinOddsOver25, over25H2H));
        }
        else
        {
            _logger.LogInformation("Over 2.5 BLOCKED: {Reasons}", string.Join(" | ", over25FilterResult.Reasons));
        }

        // 2. 2-3 Goals
        var twoToThreeH2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "2-3 Goals");
        // H2H is now informational only - no rejection
        double twoToThreeFinal = probabilities.Prob2to3Goals * twoToThreeH2H.Multiplier;
        
        // Apply European fatigue penalty
        if (homeHasEuropeanFatigue || awayHasEuropeanFatigue)
        {
            twoToThreeFinal *= EUROPEAN_FATIGUE_PENALTY;
        }
        
        candidates.Add(("2-3 Goals", probabilities.Prob2to3Goals, twoToThreeFinal, MinConfidence2to3, 2.00, twoToThreeH2H));

        // 3. BTTS (prioritized due to +17.4% historical ROI)
        var bttsH2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "BTTS Yes");
        // H2H is now informational only - no rejection
        double bttsFinal = probabilities.BTTS * bttsH2H.Multiplier;
        
        // Apply league penalty if in weak league - REMOVED, now using ExcludedLeagues for full exclusion
        // if (IsWeakLeague(league))
        // {
        //     bttsFinal /= WEAK_LEAGUE_PENALTY;
        // }
        
        // Apply European fatigue penalty
        if (homeHasEuropeanFatigue || awayHasEuropeanFatigue)
        {
            bttsFinal *= EUROPEAN_FATIGUE_PENALTY;
        }
        
        // Apply defensive failure filters for BTTS
        var bttsFilterResult = _failureFilters.ApplyBTTSFilters(matchContext);
        if (bttsFilterResult.IsAllowed)
        {
            // Use filtered confidence
            bttsFinal = bttsFilterResult.FinalConfidence;
            if (bttsFilterResult.Reasons.Any())
            {
                _logger.LogInformation("BTTS filter applied: {Reasons}", string.Join(" | ", bttsFilterResult.Reasons));
            }
            candidates.Add(("BTTS Yes", probabilities.BTTS, bttsFinal, MinConfidenceBtts, MinOddsBtts, bttsH2H));
        }
        else
        {
            _logger.LogInformation("BTTS BLOCKED: {Reasons}", string.Join(" | ", bttsFilterResult.Reasons));
        }

        // REMOVED: Home/Away Win markets - analysis shows unprofitable
        // Home Win: -7.2% ROI even when H2H allows (679 bets, 46% accuracy)
        // Away Win: -35.1% ROI even when H2H allows (675 bets, 32% accuracy)
        // Conclusion: These markets are fundamentally unpredictable

        // 4. Away Win - DISABLED
        // var awayH2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "Away Win");
        // if (awayH2H.Decision != H2HDecision.Reject)
        // {
        //     double awayFinal = probabilities.AwayWin * awayH2H.Multiplier;
        //     candidates.Add(("Away Win", probabilities.AwayWin, awayFinal, MIN_CONFIDENCE_AWAY, MIN_ODDS_AWAY, awayH2H));
        // }

        // 5. Home Win - DISABLED
        // var homeH2H = _h2hService.Evaluate(homeTeam, awayTeam, history, "Home Win");
        // if (homeH2H.Decision != H2HDecision.Reject)
        // {
        //     double homeFinal = probabilities.HomeWin * homeH2H.Multiplier;
        //     candidates.Add(("Home Win", probabilities.HomeWin, homeFinal, MIN_CONFIDENCE_HOME, MIN_ODDS_HOME, homeH2H));
        // }

        // Evaluate candidates and select best
        double bestEV = -1;
        
        foreach (var (market, rawProb, finalProb, minConf, minOdds, h2h) in candidates)
        {
            // Check confidence threshold (using dampened probability)
            if (finalProb < minConf) continue;

            // Check odds requirement (if odds available)
            double marketOdds = GetMarketOdds(market, odds);
            if (marketOdds > 0 && marketOdds < minOdds) continue;

            // Calculate expected value
            double ev = marketOdds > 0 ? (finalProb * marketOdds) - 1 : 0;
            
            // BTTS Market Bonus - prioritize due to +17.4% historical ROI vs -2.6% for Over 2.5
            if (market == "BTTS Yes") ev += BTTS_EV_BONUS;

            if (ev > bestEV)
            {
                bestEV = ev;
                decision.SelectedMarket = market;
                decision.Confidence = finalProb;
                decision.ExpectedValue = ev;
                decision.IsHighConfidence = true;
                decision.HasH2HSupport = h2h.Decision == H2HDecision.Allow;
                decision.Reasons.Clear();
                
                decision.Reasons.Add($"{market} selected with {finalProb:P1} confidence");
                
                if (h2h.Decision == H2HDecision.Dampened)
                {
                    decision.Reasons.Add($"H2H Dampened: {rawProb:P1} → {finalProb:P1} (×{h2h.Multiplier:F2})");
                    decision.Reasons.Add($"H2H Reason: {h2h.Reason}");
                }
                else
                {
                    decision.Reasons.Add($"H2H: {h2h.Reason}");
                }
                
                if (marketOdds > 0) decision.Reasons.Add($"Odds: {marketOdds:F2}, EV: {ev:P1}");
                decision.Reasons.Add($"Threshold: {minConf:P0} (passed)");
            }
        }

        // If no market passed thresholds
        if (!decision.IsHighConfidence)
        {
            decision.SelectedMarket = "No Bet";
            decision.Confidence = candidates.Any() ? candidates.Max(c => c.FinalProb) : 0;
            decision.Reasons.Add("No market passed confidence/odds thresholds after H2H filtering");
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
    
    private bool IsWeakLeague(string? league)
    {
        if (string.IsNullOrEmpty(league)) return false;
        
        // Based on backtest: E2 (League One) -22% ROI, D2 (Bundesliga 2) -10% ROI
        var weakLeagues = new[] { "E2", "D2" };
        return weakLeagues.Contains(league);
    }
    
    private MatchContext BuildMatchContext(
        string homeTeam,
        string awayTeam,
        MatchProbabilitiesDto probabilities,
        List<HistoricalMatchDto> history,
        string? league,
        MatchOddsDto? odds)
    {
        // Calculate recent form
        var homeFormHome = _recentFormService.CalculateRecentForm(homeTeam, history, isHome: true);
        var awayFormAway = _recentFormService.CalculateRecentForm(awayTeam, history, isHome: false);
        
        return new MatchContext
        {
            League = league ?? "",
            HomeXG = probabilities.ExpectedGoalsHome,
            AwayXG = probabilities.ExpectedGoalsAway,
            Over25Probability = probabilities.Over25,
            BTTSProbability = probabilities.BTTS,
            HomeOdds = (double)(odds?.HomeWin ?? 0),
            AwayOdds = (double)(odds?.AwayWin ?? 0),
            HomeCleanSheetRateLast10 = homeFormHome.CleanSheetRate,
            AwayCleanSheetRateLast10 = awayFormAway.CleanSheetRate,
            HomeFailedToScoreRateLast10 = homeFormHome.FailedToScoreRate,
            AwayFailedToScoreRateLast10 = awayFormAway.FailedToScoreRate
        };
    }
}
