using soccer_gpt_application.Interfaces;
using Microsoft.Extensions.Logging;

namespace soccer_gpt_infrastructure.Services;

public class AdvancedStatsService : IAdvancedStatsService
{
    private readonly ILogger<AdvancedStatsService> _logger;
    private readonly IH2HFilterService _h2hFilterService;
    private readonly IDecisionService _decisionService;
    private const int SimulationRuns = 5000;
    private const double Rho = -0.13; 

    public AdvancedStatsService(
        ILogger<AdvancedStatsService> logger, 
        IH2HFilterService h2hFilterService,
        IDecisionService decisionService)
    {
        _logger = logger;
        _h2hFilterService = h2hFilterService;
        _decisionService = decisionService;
    }

    private const double DecayFactor = 0.005; // xi value for Time Decay (approx half-life 140 days)

    public Task<AdvancedAnalyticsDto> CalculateAnalyticsAsync(string homeTeam, string awayTeam, List<HistoricalMatchDto> allHistory)
    {
        // 1. Get League Context (last 1000 matches for robust averages)
        // We use weighted averages even for league to prioritize recent context
        var recentGlobal = allHistory.OrderByDescending(m => m.Date).Take(1000).ToList();
        var refDate = recentGlobal.FirstOrDefault()?.Date ?? DateTime.Today;

        double leagueAvgHomeGoals = CalculateWeightedAverage(recentGlobal, m => m.FTHG, refDate);
        double leagueAvgAwayGoals = CalculateWeightedAverage(recentGlobal, m => m.FTAG, refDate);
        double leagueAvgHomeHST = CalculateWeightedAverage(recentGlobal, m => m.HST, refDate);
        double leagueAvgAwayHST = CalculateWeightedAverage(recentGlobal, m => m.AST, refDate);
        double leagueAvgHomeCorners = CalculateWeightedAverage(recentGlobal, m => m.HC, refDate);
        double leagueAvgAwayCorners = CalculateWeightedAverage(recentGlobal, m => m.AC, refDate);

        // 2. Calculate Team Ratings (Time-Weighted + Shot-Based)
        // Home Team Att (using their Home matches) vs Away Team Def (using their Away matches)
        var homeAtt = CalculateCombinedStrength(homeTeam, true, true, allHistory, refDate, leagueAvgHomeGoals, leagueAvgHomeHST, leagueAvgHomeCorners);
        var homeDef = CalculateCombinedStrength(homeTeam, true, false, allHistory, refDate, leagueAvgAwayGoals, leagueAvgAwayHST, leagueAvgAwayCorners);
        
        var awayAtt = CalculateCombinedStrength(awayTeam, false, true, allHistory, refDate, leagueAvgAwayGoals, leagueAvgAwayHST, leagueAvgAwayCorners);
        var awayDef = CalculateCombinedStrength(awayTeam, false, false, allHistory, refDate, leagueAvgHomeGoals, leagueAvgHomeHST, leagueAvgHomeCorners);

        // Expected Goals
        // Formula: TeamAtt * OppDef * LeagueAvg
        double homeExp = homeAtt * awayDef * leagueAvgHomeGoals;
        double awayExp = awayAtt * homeDef * leagueAvgAwayGoals;
        
        if (homeExp < 0.1) homeExp = 0.1; 
        if (awayExp < 0.1) awayExp = 0.1;

        // 3. Probabilities
        var probs = CalculateProbabilities(homeExp, awayExp);

        // 4. Monte Carlo
        var streakAnalysis = PerformMonteCarloSimulation(homeExp, awayExp, homeTeam, awayTeam, allHistory);

        // 5. H2H Filter Analysis
        var h2hAnalysis = _h2hFilterService.AnalyzeH2H(homeTeam, awayTeam, allHistory);

        // 6. Decision Layer
        var decision = _decisionService.MakeDecision(probs, h2hAnalysis);

        return Task.FromResult(new AdvancedAnalyticsDto
        {
            Probabilities = probs,
            StreakAnalysis = streakAnalysis,
            H2HAnalysis = h2hAnalysis,
            Decision = decision
        });
    }

    private double CalculateCombinedStrength(string team, bool isHomeRole, bool isAttackStrength, List<HistoricalMatchDto> allMatches, DateTime refDate, double lgGoals, double lgHST, double lgCorners)
    {
        // 1. Filter matches where team played in the specific role (Home or Away)
        var teamMatches = allMatches
            .Where(m => IsMatch(isHomeRole ? m.HomeTeam : m.AwayTeam, team))
            .OrderByDescending(m => m.Date)
            .Take(50) // Take last 50 matches in this role for sample
            .ToList();

        if (teamMatches.Count < 3) return 1.0; // Not enough data

        // 2. Calculate Weighted Averages for Team
        // If Attack Strength: We want Goals Scored (FTHG if Home, FTAG if Away)
        // If Defense Strength: We want Goals Conceded (FTAG if Home, FTHG if Away)
        
        Func<HistoricalMatchDto, double> goalSelector;
        Func<HistoricalMatchDto, double> hstSelector;
        Func<HistoricalMatchDto, double> cornerSelector;

        if (isAttackStrength)
        {
            // Scored
            goalSelector = m => isHomeRole ? m.FTHG : m.FTAG;
            hstSelector = m => isHomeRole ? m.HST : m.AST;
            cornerSelector = m => isHomeRole ? m.HC : m.AC;
        }
        else
        {
            // Conceded
            goalSelector = m => isHomeRole ? m.FTAG : m.FTHG;
            hstSelector = m => isHomeRole ? m.AST : m.HST; // Shots faced
            cornerSelector = m => isHomeRole ? m.AC : m.HC; // Corners faced
        }

        double teamAvgGoals = CalculateWeightedAverage(teamMatches, goalSelector, refDate);
        double teamAvgHST = CalculateWeightedAverage(teamMatches, hstSelector, refDate);
        double teamAvgCorners = CalculateWeightedAverage(teamMatches, cornerSelector, refDate);

        // 3. Calculate Relative Strengths vs League
        double relGoals = teamAvgGoals / (lgGoals == 0 ? 1 : lgGoals);
        double relHST = teamAvgHST / (lgHST == 0 ? 1 : lgHST);
        double relCorners = teamAvgCorners / (lgCorners == 0 ? 1 : lgCorners);

        // 4. Weighted Power Formula
        // 50% Goals (Results), 30% HST (Quality), 20% Corners (Pressure)
        double rating = (relGoals * 0.5) + (relHST * 0.3) + (relCorners * 0.2);
        
        return rating;
    }

    private double CalculateWeightedAverage(List<HistoricalMatchDto> matches, Func<HistoricalMatchDto, double> selector, DateTime refDate)
    {
        double sumWeightedVal = 0;
        double sumWeights = 0;

        foreach (var m in matches)
        {
            double val = selector(m);
            double daysAgo = (refDate - m.Date).TotalDays;
            if (daysAgo < 0) daysAgo = 0; // Future safety
            
            double weight = Math.Exp(-DecayFactor * daysAgo);
            
            sumWeightedVal += val * weight;
            sumWeights += weight;
        }

        return sumWeights > 0 ? sumWeightedVal / sumWeights : 0;
    }

    private MatchProbabilitiesDto CalculateProbabilities(double homeExp, double awayExp)
    {
        double pHome = 0, pDraw = 0, pAway = 0;
        double pOver15 = 0, pOver25 = 0, pBTTS = 0, p2to3 = 0;

        for (int i = 0; i <= 9; i++) 
        {
            for (int j = 0; j <= 9; j++) 
            {
                double prob = Poisson(i, homeExp) * Poisson(j, awayExp);
                prob = ApplyDixonColesAdjustment(prob, i, j, homeExp, awayExp);

                if (i > j) pHome += prob;
                else if (i == j) pDraw += prob;
                else pAway += prob;

                int total = i + j;
                if (total > 1.5) pOver15 += prob;
                if (total > 2.5) pOver25 += prob;
                if (i > 0 && j > 0) pBTTS += prob;
                if (total == 2 || total == 3) p2to3 += prob;
            }
        }

        return new MatchProbabilitiesDto
        {
            HomeWin = Math.Round(pHome, 4),
            Draw = Math.Round(pDraw, 4),
            AwayWin = Math.Round(pAway, 4),
            Over15 = Math.Round(pOver15, 4),
            Over25 = Math.Round(pOver25, 4),
            BTTS = Math.Round(pBTTS, 4),
            ExpectedGoalsHome = Math.Round(homeExp, 2),
            ExpectedGoalsAway = Math.Round(awayExp, 2),
            Prob2to3Goals = Math.Round(p2to3, 4)
        };
    }

    private double Poisson(int k, double lambda)
    {
        return (Math.Pow(lambda, k) * Math.Exp(-lambda)) / Factorial(k);
    }

    private double ApplyDixonColesAdjustment(double prob, int x, int y, double hExp, double aExp)
    {
        if (x == 0 && y == 0) return prob * (1.0 - (hExp * aExp * Rho));
        if (x == 0 && y == 1) return prob * (1.0 + (hExp * Rho));
        if (x == 1 && y == 0) return prob * (1.0 + (aExp * Rho));
        if (x == 1 && y == 1) return prob * (1.0 - Rho);
        return prob;
    }

    private long Factorial(int n)
    {
        if (n <= 1) return 1;
        long result = 1;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }

    private StreakAnalysisDto PerformMonteCarloSimulation(double hExp, double aExp, string homeTeam, string awayTeam, List<HistoricalMatchDto> history)
    {
        // 1. Identify Recent Streak
        var recentMatches = history.Where(m => IsMatch(m.HomeTeam, homeTeam) || IsMatch(m.AwayTeam, homeTeam))
                                .OrderByDescending(m => m.Date).Take(5).ToList();
        
        // 2. Monte Carlo Simulation
        int simHome = 0, simDraw = 0, simAway = 0;
        int simOver15 = 0, simOver25 = 0, simBTTS = 0;
        Random rnd = new Random();
        
        for(int i = 0; i < SimulationRuns; i++)
        {
            int simHG = SimulatePoisson(rnd, hExp);
            int simAG = SimulatePoisson(rnd, aExp);
            
            if (simHG > simAG) simHome++;
            else if (simHG == simAG) simDraw++;
            else simAway++;

            if ((simHG + simAG) > 1.5) simOver15++;
            if ((simHG + simAG) > 2.5) simOver25++;
            if (simHG > 0 && simAG > 0) simBTTS++;
        }
        
        double probHome = (double)simHome / SimulationRuns;
        double probDraw = (double)simDraw / SimulationRuns;
        double probAway = (double)simAway / SimulationRuns;
        double probOver15 = (double)simOver15 / SimulationRuns;
        double probOver25 = (double)simOver25 / SimulationRuns;
        double probBTTS = (double)simBTTS / SimulationRuns;
        
        // 3. Status Check (Home Win Focus for simplicity, or aggregation)
        // Just return "Simulated Probabilities" as the Edge reference.
        // Reversion Index based on Home Win Rate deviation
        int actualWins = 0;
        foreach(var m in recentMatches) {
            bool isHome = IsMatch(m.HomeTeam, homeTeam);
            if ((isHome && m.FTR == "H") || (!isHome && m.FTR == "A")) actualWins++;
        }
        double recentWinRate = (double)actualWins / (recentMatches.Count == 0 ? 1 : recentMatches.Count);
        
        string status = "Neutral";
        double reversion = 0;
        if (recentWinRate > (probHome + 0.25)) { status = "Overperforming"; reversion = recentWinRate - probHome; }
        else if (recentWinRate < (probHome - 0.25)) { status = "Underperforming"; reversion = probHome - recentWinRate; }

        return new StreakAnalysisDto
        {
            Status = status,
            ReversionIndex = Math.Round(reversion, 2),
            MonteCarloConfidence = 0.85, 
            EdgeHomeWin = Math.Round(probHome, 2),
            EdgeDraw = Math.Round(probDraw, 2),
            EdgeAwayWin = Math.Round(probAway, 2),
            EdgeOver15 = Math.Round(probOver15, 2),
            EdgeOver25 = Math.Round(probOver25, 2),
            EdgeBTTS = Math.Round(probBTTS, 2)
        };
    }
    
    private int SimulatePoisson(Random rnd, double lambda)
    {
        double L = Math.Exp(-lambda);
        double p = 1.0;
        int k = 0;
        do
        {
            k++;
            p *= rnd.NextDouble();
        } while (p > L);
        return k - 1;
    }

    private bool IsMatch(string s1, string s2)
    {
         if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;
        if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return true;
        return s1.Contains(s2, StringComparison.OrdinalIgnoreCase) || s2.Contains(s1, StringComparison.OrdinalIgnoreCase);
    }
}
