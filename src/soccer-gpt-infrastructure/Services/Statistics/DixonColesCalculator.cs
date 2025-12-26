using System;
using Microsoft.Extensions.Logging;

namespace soccer_gpt_infrastructure.Services.Statistics;

/// <summary>
/// Dixon-Coles Poisson calculator for soccer match predictions
/// Corrects for underestimation of low-scoring draws (0-0, 1-1)
/// </summary>
public class DixonColesCalculator
{
    private readonly ILogger<DixonColesCalculator> _logger;
    private const double DefaultRho = -0.13; // Empirically derived correlation parameter
    private const int MaxGoals = 5; // Calculate up to 5 goals for each team
    
    public DixonColesCalculator(ILogger<DixonColesCalculator> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Calculate Dixon-Coles adjusted probabilities for match outcomes
    /// </summary>
    /// <param name="homeExpectedGoals">Expected goals for home team (lambda)</param>
    /// <param name="awayExpectedGoals">Expected goals for away team (mu)</param>
    /// <param name="rho">Correlation parameter (default -0.13)</param>
    /// <returns>Probabilities for Home Win, Draw, Away Win, and BTTS</returns>
    public DixonColesProbabilities CalculateProbabilities(
        double homeExpectedGoals,
        double awayExpectedGoals,
        double? rho = null)
    {
        var rhoValue = rho ?? DefaultRho;
        
        // Build probability matrix for all score combinations (0-5 each)
        var scoreMatrix = new double[MaxGoals + 1, MaxGoals + 1];
        
        for (int homeGoals = 0; homeGoals <= MaxGoals; homeGoals++)
        {
            for (int awayGoals = 0; awayGoals <= MaxGoals; awayGoals++)
            {
                // Standard Poisson probability
                double poissonProb = PoissonProbability(homeGoals, homeExpectedGoals) 
                                   * PoissonProbability(awayGoals, awayExpectedGoals);
                
                // Apply Dixon-Coles adjustment for low-scoring games
                double adjustment = GetDixonColesAdjustment(homeGoals, awayGoals, 
                    homeExpectedGoals, awayExpectedGoals, rhoValue);
                
                scoreMatrix[homeGoals, awayGoals] = poissonProb * adjustment;
            }
        }
        
        // Aggregate probabilities
        double homeWinProb = 0;
        double drawProb = 0;
        double awayWinProb = 0;
        double bttsProb = 0;
        
        for (int h = 0; h <= MaxGoals; h++)
        {
            for (int a = 0; a <= MaxGoals; a++)
            {
                double prob = scoreMatrix[h, a];
                
                if (h > a)
                    homeWinProb += prob;
                else if (h == a)
                    drawProb += prob;
                else
                    awayWinProb += prob;
                
                // BTTS: both teams score at least 1
                if (h >= 1 && a >= 1)
                    bttsProb += prob;
            }
        }
        
        // Normalize to ensure probabilities sum to ~1.0
        double total = homeWinProb + drawProb + awayWinProb;
        homeWinProb /= total;
        drawProb /= total;
        awayWinProb /= total;
        
        _logger.LogDebug("Dixon-Coles: H={HomeWin:P1}, D={Draw:P1}, A={AwayWin:P1}, BTTS={BTTS:P1}",
            homeWinProb, drawProb, awayWinProb, bttsProb);
        
        return new DixonColesProbabilities
        {
            HomeWin = homeWinProb,
            Draw = drawProb,
            AwayWin = awayWinProb,
            BTTS = bttsProb,
            HomeExpectedGoals = homeExpectedGoals,
            AwayExpectedGoals = awayExpectedGoals
        };
    }
    
    /// <summary>
    /// Dixon-Coles adjustment factor
    /// Corrects underestimation of low-scoring draws
    /// </summary>
    private double GetDixonColesAdjustment(
        int homeGoals, 
        int awayGoals,
        double lambda, 
        double mu, 
        double rho)
    {
        // Only adjust for scores 0-0, 0-1, 1-0, 1-1
        if (homeGoals > 1 || awayGoals > 1)
            return 1.0;
        
        // tau(x, y, lambda, mu, rho)
        double lambdaMu = lambda * mu;
        
        if (homeGoals == 0 && awayGoals == 0)
            return 1 - lambdaMu * rho;
        
        if (homeGoals == 0 && awayGoals == 1)
            return 1 + lambda * rho;
        
        if (homeGoals == 1 && awayGoals == 0)
            return 1 + mu * rho;
        
        if (homeGoals == 1 && awayGoals == 1)
            return 1 - rho;
        
        return 1.0;
    }
    
    /// <summary>
    /// Standard Poisson probability: P(X = k) = (λ^k * e^(-λ)) / k!
    /// </summary>
    private double PoissonProbability(int goals, double expectedGoals)
    {
        if (expectedGoals <= 0)
            return goals == 0 ? 1.0 : 0.0;
        
        // P(X = k) = (λ^k / k!) * e^(-λ)
        double logProb = goals * Math.Log(expectedGoals) 
                       - expectedGoals 
                       - LogFactorial(goals);
        
        return Math.Exp(logProb);
    }
    
    /// <summary>
    /// Calculate log(n!) for factorial in log space (more numerically stable)
    /// </summary>
    private double LogFactorial(int n)
    {
        if (n <= 1) return 0;
        
        double result = 0;
        for (int i = 2; i <= n; i++)
            result += Math.Log(i);
        
        return result;
    }
}

/// <summary>
/// Result of Dixon-Coles probability calculation
/// </summary>
public class DixonColesProbabilities
{
    public double HomeWin { get; set; }
    public double Draw { get; set; }
    public double AwayWin { get; set; }
    public double BTTS { get; set; }
    public double HomeExpectedGoals { get; set; }
    public double AwayExpectedGoals { get; set; }
}
