using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Poisson's probability calculator with Dixon-Coles low-scoring adjustment
/// </summary>
public class PoissonCalculationService(
    IApplicationDbContext dbContext,
    ILogger<PoissonCalculationService> logger) : IPoissonCalculationService
{
    private const int MinMatchesForCalculation = 10;
    private const double DixonColesRho = -0.13; // Low-scoring correlation correction

    /// <summary>
    /// Calculate match probabilities using Dixon-Coles Poisson model
    /// </summary>
    public async Task<PoissonProbabilities?> CalculateProbabilitiesAsync(
        int leagueId, int homeTeamId, int awayTeamId, DateTime matchDate, CancellationToken ct = default)
    {
        try
        {
            // Get league averages from completed fixtures before match date
            var leagueAvg = await GetLeagueAveragesAsync(leagueId, matchDate, ct);
            if (leagueAvg.MatchesAnalyzed < MinMatchesForCalculation)
            {
                logger.LogWarning("Insufficient matches for league {LeagueId}: {Count}", leagueId, leagueAvg.MatchesAnalyzed);
                return null;
            }

            // Get team strengths
            var homeStrength = await GetTeamStrengthAsync(leagueId, homeTeamId, matchDate, ct);
            var awayStrength = await GetTeamStrengthAsync(leagueId, awayTeamId, matchDate, ct);

            if (homeStrength == null || awayStrength == null)
            {
                logger.LogWarning("Insufficient data for teams {Home}/{Away}", homeTeamId, awayTeamId);
                return null;
            }

            // Calculate expected goals: lambda = attack * defense * league_avg
            double lambdaHome = homeStrength.AttackStrength * awayStrength.DefenseStrength * leagueAvg.HomeGoalsAvg;
            double lambdaAway = awayStrength.AttackStrength * homeStrength.DefenseStrength * leagueAvg.AwayGoalsAvg;

            // Ensure reasonable bounds
            lambdaHome = Math.Clamp(lambdaHome, 0.3, 4.0);
            lambdaAway = Math.Clamp(lambdaAway, 0.2, 3.5);

            // Calculate score probabilities with Dixon-Coles adjustment
            var (homeWin, draw, awayWin, over25, btts, goals23, score00, score10, score01) = 
                CalculateOutcomes(lambdaHome, lambdaAway);

            return new PoissonProbabilities
            {
                HomeWin = homeWin,
                Draw = draw,
                AwayWin = awayWin,
                Over25 = over25,
                Under25 = 1 - over25,
                BothTeamScoredGoal = btts,
                BTTSNo = 1 - btts,
                TwoToThreeGoals = goals23,
                HomeExpectedGoals = lambdaHome,
                AwayExpectedGoals = lambdaAway,
                Score00 = score00,
                Score10 = score10,
                Score01 = score01
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calculating probabilities for {Home} vs {Away}", homeTeamId, awayTeamId);
            return null;
        }
    }

    /// <summary>
    /// Get league average goals from completed fixtures
    /// </summary>
    public async Task<LeagueAverages> GetLeagueAveragesAsync(int leagueId, DateTime beforeDate, CancellationToken ct = default)
    {
        var matches = await dbContext.Fixtures
            .Where(f => f.LeagueId == leagueId && 
                       f.Status == "FT" && 
                       f.IsCurrentSeason &&
                       f.Date < beforeDate)
            .Select(f => new { f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return new LeagueAverages(leagueId, 1.45, 1.15, 0); // Default averages

        double homeAvg = matches.Average(m => m.HomeGoal);
        double awayAvg = matches.Average(m => m.AwayGoal);

        return new LeagueAverages(leagueId, homeAvg, awayAvg, matches.Count);
    }

    /// <summary>
    /// Calculate team attack/defense strength relative to league average
    /// </summary>
    public async Task<TeamStrength?> GetTeamStrengthAsync(int leagueId, int teamId, DateTime beforeDate, CancellationToken ct = default)
    {
        // Get league averages first
        var leagueAvg = await GetLeagueAveragesAsync(leagueId, beforeDate, ct);
        if (leagueAvg.MatchesAnalyzed < MinMatchesForCalculation)
            return null;

        // Get team's home matches
        var homeMatches = await dbContext.Fixtures
            .Where(f => f.LeagueId == leagueId && 
                       f.HomeTeamId == teamId && 
                       f.Status == "FT" && 
                       f.IsCurrentSeason &&
                       f.Date < beforeDate)
            .Select(f => new { Scored = f.HomeGoal, Conceded = f.AwayGoal })
            .ToListAsync(ct);

        // Get team's away matches
        var awayMatches = await dbContext.Fixtures
            .Where(f => f.LeagueId == leagueId && 
                       f.AwayTeamId == teamId && 
                       f.Status == "FT" && 
                       f.IsCurrentSeason &&
                       f.Date < beforeDate)
            .Select(f => new { Scored = f.AwayGoal, Conceded = f.HomeGoal })
            .ToListAsync(ct);

        int totalMatches = homeMatches.Count + awayMatches.Count;
        if (totalMatches < 3)
            return null;

        // Calculate team averages
        double teamScoredHome = homeMatches.Count > 0 ? homeMatches.Average(m => m.Scored) : leagueAvg.HomeGoalsAvg;
        double teamConcededHome = homeMatches.Count > 0 ? homeMatches.Average(m => m.Conceded) : leagueAvg.AwayGoalsAvg;
        double teamScoredAway = awayMatches.Count > 0 ? awayMatches.Average(m => m.Scored) : leagueAvg.AwayGoalsAvg;
        double teamConcededAway = awayMatches.Count > 0 ? awayMatches.Average(m => m.Conceded) : leagueAvg.HomeGoalsAvg;

        // Attack strength: team's goals scored / league average
        // Defense strength: team's goals conceded / league average (lower is better)
        double attackStrength = ((teamScoredHome / leagueAvg.HomeGoalsAvg) + (teamScoredAway / leagueAvg.AwayGoalsAvg)) / 2;
        double defenseStrength = ((teamConcededHome / leagueAvg.AwayGoalsAvg) + (teamConcededAway / leagueAvg.HomeGoalsAvg)) / 2;

        // Clamp to reasonable bounds
        attackStrength = Math.Clamp(attackStrength, 0.5, 2.0);
        defenseStrength = Math.Clamp(defenseStrength, 0.5, 2.0);

        return new TeamStrength(teamId, attackStrength, defenseStrength, totalMatches);
    }

    /// <summary>
    /// Calculate all outcome probabilities using Dixon-Coles adjusted Poisson
    /// </summary>
    private (double homeWin, double draw, double awayWin, double over25, double btts, double goals23, double score00, double score10, double score01) 
        CalculateOutcomes(double lambdaHome, double lambdaAway)
    {
        double homeWin = 0, draw = 0, awayWin = 0;
        double over25 = 0, under25 = 0;
        double bttsYes = 0, bttsNo = 0;
        double goals23 = 0;
        double[,] scoreProbs = new double[7, 7]; // Up to 6-6

        // Calculate score matrix with Dixon-Coles adjustment
        for (int h = 0; h < 7; h++)
        {
            for (int a = 0; a < 7; a++)
            {
                double prob = Poisson(h, lambdaHome) * Poisson(a, lambdaAway);
                
                // Dixon-Coles low-scoring correction
                if (h <= 1 && a <= 1)
                    prob *= DixonColesAdjustment(h, a, lambdaHome, lambdaAway);

                scoreProbs[h, a] = prob;

                // Aggregate outcomes
                if (h > a) homeWin += prob;
                else if (h < a) awayWin += prob;
                else draw += prob;

                int total = h + a;
                if (total > 2) over25 += prob;
                else under25 += prob;

                if (h > 0 && a > 0) bttsYes += prob;
                else bttsNo += prob;

                if (total == 2 || total == 3) goals23 += prob;
            }
        }

        // Normalize probabilities
        double total1 = homeWin + draw + awayWin;
        homeWin /= total1; draw /= total1; awayWin /= total1;

        return (homeWin, draw, awayWin, over25, bttsYes, goals23, scoreProbs[0, 0], scoreProbs[1, 0], scoreProbs[0, 1]);
    }

    /// <summary>
    /// Poisson probability mass function
    /// </summary>
    private static double Poisson(int k, double lambda)
    {
        return Math.Pow(lambda, k) * Math.Exp(-lambda) / Factorial(k);
    }

    /// <summary>
    /// Factorial for small numbers
    /// </summary>
    private static double Factorial(int n)
    {
        if (n <= 1) return 1;
        double result = 1;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }

    /// <summary>
    /// Dixon-Coles adjustment for low-scoring matches (0-0, 1-0, 0-1, 1-1)
    /// </summary>
    private static double DixonColesAdjustment(int homeGoals, int awayGoals, double lambdaHome, double lambdaAway)
    {
        double rho = DixonColesRho;
        
        return (homeGoals, awayGoals) switch
        {
            (0, 0) => 1 - lambdaHome * lambdaAway * rho,
            (0, 1) => 1 + lambdaHome * rho,
            (1, 0) => 1 + lambdaAway * rho,
            (1, 1) => 1 - rho,
            _ => 1.0
        };
    }
}
