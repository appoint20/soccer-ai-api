using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class AdvancedStatsService : IAdvancedStatsService
{
    private const double Rho = -0.13;

    private const double DecayFactor = 0.005;

    public Task<PoissonProbabilitiesDto> CalculateAnalyticsAsync(
        string homeTeam, 
        string awayTeam, 
        List<HistoricalMatchDto> allHistory,
        string? league = null)
    {
        // 1. Get League Context (last 1000 matches for robust averages)
        // We use weighted averages even for league to prioritize recent context
        var recentGlobal = allHistory.OrderByDescending(m => m.Date).Take(100).ToList();
        var refDate = recentGlobal.FirstOrDefault()?.Date ?? DateTime.Today;

        var leagueAvgHomeGoals = CalculateWeightedAverage(recentGlobal, m => m.FTHG, refDate);
        var leagueAvgAwayGoals = CalculateWeightedAverage(recentGlobal, m => m.FTAG, refDate);

        // 2. Calculate Team Ratings (Time-Weighted + Shot-Based)
        // Home Team Att (using their Home matches) vs Away Team Def (using their Away matches)
        var homeAtt = CalculateCombinedStrength(homeTeam, true, true, allHistory, refDate, leagueAvgHomeGoals);
        var homeDef = CalculateCombinedStrength(homeTeam, true, false, allHistory, refDate, leagueAvgAwayGoals);
        
        var awayAtt = CalculateCombinedStrength(awayTeam, false, true, allHistory, refDate, leagueAvgAwayGoals);
        var awayDef = CalculateCombinedStrength(awayTeam, false, false, allHistory, refDate, leagueAvgHomeGoals);

        // Expected Goals
        // Formula: TeamAtt * OppDef * LeagueAvg
        var homeExp = homeAtt * awayDef * leagueAvgHomeGoals;
        var awayExp = awayAtt * homeDef * leagueAvgAwayGoals;
        
        if (homeExp < 0.1) homeExp = 0.1; 
        if (awayExp < 0.1) awayExp = 0.1;

        // 3. Probabilities
        var probabilities = CalculateProbabilities(homeExp, awayExp);

        return Task.FromResult(new PoissonProbabilitiesDto
        {
            HomeWin = probabilities.HomeWin,
            Draw = probabilities.Draw,
            AwayWin = probabilities.AwayWin,
            Over15 = probabilities.Over15,
            Over25 = probabilities.Over25,
            BTTS = probabilities.BTTS,
            ExpectedGoalsHome = probabilities.ExpectedGoalsHome,
            ExpectedGoalsAway = probabilities.ExpectedGoalsAway,
            Prob2to3Goals = probabilities.Prob2to3Goals
        });
    }

    private static double CalculateCombinedStrength(string team, bool isHomeRole, bool isAttackStrength, List<HistoricalMatchDto> allMatches, DateTime refDate, double lgGoals)
    {
        // 1. Filter matches where team played in the specific role (Home or Away)
        var teamMatches = allMatches
            .Where(m => IsMatch(isHomeRole ? m.HomeTeam : m.AwayTeam, team))
            .OrderByDescending(m => m.Date)
            .Take(50) // Take last 50 matches in this role for sample
            .ToList();

        if (teamMatches.Count < 3) return 1.0; // Not enough data

        // 2. Calculate Weighted Averages for Team
        
        Func<HistoricalMatchDto, double> goalSelector;

        if (isAttackStrength)
        {
            // Scored
            goalSelector = m => isHomeRole ? m.FTHG : m.FTAG;
        }
        else
        {
            // Conceded
            goalSelector = m => isHomeRole ? m.FTAG : m.FTHG;
        }

        var teamAvgGoals = CalculateWeightedAverage(teamMatches, goalSelector, refDate);

        var relGoals = teamAvgGoals / (lgGoals == 0 ? 1 : lgGoals);
        
        return relGoals;
    }

    private static double CalculateWeightedAverage(List<HistoricalMatchDto> matches, Func<HistoricalMatchDto, double> selector, DateTime refDate)
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

    private PoissonProbabilitiesDto CalculateProbabilities(double homeExp, double awayExp)
    {
        double pHome = 0, pDraw = 0, pAway = 0;
        double pOver15 = 0, pOver25 = 0, pBtts = 0, p2To3 = 0;

        for (var i = 0; i <= 9; i++) 
        {
            for (var j = 0; j <= 9; j++) 
            {
                var prob = Poisson(i, homeExp) * Poisson(j, awayExp);
                prob = ApplyDixonColesAdjustment(prob, i, j, homeExp, awayExp);

                if (i > j) pHome += prob;
                else if (i == j) pDraw += prob;
                else pAway += prob;

                var total = i + j;
                if (total > 1.5) pOver15 += prob;
                if (total > 2.5) pOver25 += prob;
                if (i > 0 && j > 0) pBtts += prob;
                if (total is 2 or 3) p2To3 += prob;
            }
        }

        return new PoissonProbabilitiesDto
        {
            HomeWin = Math.Round(pHome, 4),
            Draw = Math.Round(pDraw, 4),
            AwayWin = Math.Round(pAway, 4),
            Over15 = Math.Round(pOver15, 4),
            Over25 = Math.Round(pOver25, 4),
            BTTS = Math.Round(pBtts, 4),
            ExpectedGoalsHome = Math.Round(homeExp, 2),
            ExpectedGoalsAway = Math.Round(awayExp, 2),
            Prob2to3Goals = Math.Round(p2To3, 4)
        };
    }

    private static double Poisson(int k, double lambda)
    {
        return Math.Pow(lambda, k) * Math.Exp(-lambda) / Factorial(k);
    }

    private double ApplyDixonColesAdjustment(double prob, int x, int y, double hExp, double aExp)
    {
        return x switch
        {
            0 when y == 0 => prob * (1.0 - hExp * aExp * Rho),
            0 when y == 1 => prob * (1.0 + hExp * Rho),
            1 when y == 0 => prob * (1.0 + aExp * Rho),
            1 when y == 1 => prob * (1.0 - Rho),
            _ => prob
        };
    }

    private static long Factorial(int n)
    {
        if (n <= 1) return 1;
        long result = 1;
        for (var i = 2; i <= n; i++) result *= i;
        return result;
    }

    private static bool IsMatch(string s1, string s2)
    {
         if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;
        if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return true;
        return s1.Contains(s2, StringComparison.OrdinalIgnoreCase) || s2.Contains(s1, StringComparison.OrdinalIgnoreCase);
    }
}