using Microsoft.Extensions.Logging;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Statistics;

/// <summary>
/// Enhanced value betting service supporting multiple concurrent predictions
/// Implements Dixon-Coles Poisson with EV-based 1X2 and threshold-based goal markets
/// </summary>
public class ValueBettingService
{
    private readonly DixonColesCalculator _dixonColes;
    private readonly ILogger<ValueBettingService> _logger;
    
    // Strategy thresholds
    private const double MinimumEV = 0.05; // 5% edge required for 1X2
    private const double MinimumBTTSProbability = 0.60; // 60% confidence for BTTS
    private const double MinimumOver25Probability = 0.55; // 55% confidence for Over 2.5
    private const double MinimumOdds = 1.01; // Sanity check
    
    public ValueBettingService(
        DixonColesCalculator dixonColes,
        ILogger<ValueBettingService> logger)
    {
        _dixonColes = dixonColes;
        _logger = logger;
    }
    
    /// <summary>
    /// Make betting decisions - can return multiple predictions for same match
    /// </summary>
    public PredictionResult MakePredictions(
        double homeExpectedGoals,
        double awayExpectedGoals,
        MatchOdds? odds = null)
    {
        // Calculate Dixon-Coles probabilities
        var probabilities = _dixonColes.CalculateProbabilities(
            homeExpectedGoals, 
            awayExpectedGoals);
        
        var result = new PredictionResult
        {
            HomeWinProbability = probabilities.HomeWin,
            DrawProbability = probabilities.Draw,
            AwayWinProbability = probabilities.AwayWin,
            BTTSProbability = probabilities.BTTS,
            Over25Probability = CalculateOver25Probability(probabilities, homeExpectedGoals, awayExpectedGoals)
        };
        
        // PRIMARY: 1X2 Market (value-based if odds available)
        if (odds != null && HasValid1X2Odds(odds))
        {
            var bestMarket = EvaluateMatchResultMarket(probabilities, odds);
            if (bestMarket != null)
            {
                result.Predictions.Add(new MarketPrediction
                {
                    Market = bestMarket.Market,
                    Probability = bestMarket.Probability,
                    ExpectedValue = bestMarket.EV,
                    Odds = bestMarket.Odds,
                    Strategy = "Value Betting (1X2)",
                    Reason = bestMarket.Reason
                });
            }
        }
        
        // SECONDARY: BTTS Market (threshold-based)
        if (probabilities.BTTS >= MinimumBTTSProbability)
        {
            result.Predictions.Add(new MarketPrediction
            {
                Market = "BTTS Yes",
                Probability = probabilities.BTTS,
                ExpectedValue = 0,
                Odds = 0,
                Strategy = "Confidence Threshold",
                Reason = $"BTTS {probabilities.BTTS:P1} ≥ {MinimumBTTSProbability:P0} threshold"
            });
        }
        
        // TERTIARY: Over 2.5 Goals (threshold-based)
        if (result.Over25Probability >= MinimumOver25Probability)
        {
            result.Predictions.Add(new MarketPrediction
            {
                Market = "Over 2.5 Goals",
                Probability = result.Over25Probability,
                ExpectedValue = 0,
                Odds = 0,
                Strategy = "Confidence Threshold",
                Reason = $"Over 2.5 {result.Over25Probability:P1} ≥ {MinimumOver25Probability:P0} threshold"
            });
        }
        
        // Log result
        if (result.Predictions.Any())
        {
            _logger.LogInformation("Match predictions: {Count} markets selected", result.Predictions.Count);
            foreach (var pred in result.Predictions)
            {
                _logger.LogInformation("  - {Market}: {Prob:P1} (EV: {EV:P1})", 
                    pred.Market, pred.Probability, pred.ExpectedValue);
            }
        }
        else
        {
            _logger.LogInformation("No value found - no predictions made");
        }
        
        return result;
    }
    
    /// <summary>
    /// Calculate Over 2.5 probability from Dixon-Coles matrix
    /// </summary>
    private double CalculateOver25Probability(
        DixonColesProbabilities probabilities,
        double homeExpectedGoals,
        double awayExpectedGoals)
    {
        // Recalculate from score matrix for accuracy
        double over25Prob = 0;
        
        for (int h = 0; h <= 5; h++)
        {
            for (int a = 0; a <= 5; a++)
            {
                if (h + a >= 3)
                {
                    double prob = PoissonProbability(h, homeExpectedGoals) 
                                * PoissonProbability(a, awayExpectedGoals);
                    over25Prob += prob;
                }
            }
        }
        
        return over25Prob;
    }
    
    /// <summary>
    /// Evaluate 1X2 for value bets
    /// </summary>
    private MarketEvaluation? EvaluateMatchResultMarket(
        DixonColesProbabilities probabilities,
        MatchOdds odds)
    {
        var markets = new List<MarketEvaluation>();
        
        // Home Win
        if ((double)odds.HomeWin >= MinimumOdds)
        {
            double ev = CalculateEV(probabilities.HomeWin, (double)odds.HomeWin);
            markets.Add(new MarketEvaluation
            {
                Market = "Home Win",
                Probability = probabilities.HomeWin,
                Odds = (double)odds.HomeWin,
                EV = ev,
                Reason = $"Home {probabilities.HomeWin:P1} @ {odds.HomeWin:F2} = EV {ev:P1}"
            });
        }
        
        // Draw
        if ((double)odds.Draw >= MinimumOdds)
        {
            double ev = CalculateEV(probabilities.Draw, (double)odds.Draw);
            markets.Add(new MarketEvaluation
            {
                Market = "Draw",
                Probability = probabilities.Draw,
                Odds = (double)odds.Draw,
                EV = ev,
                Reason = $"Draw {probabilities.Draw:P1} @ {odds.Draw:F2} = EV {ev:P1}"
            });
        }
        
        // Away Win
        if ((double)odds.AwayWin >= MinimumOdds)
        {
            double ev = CalculateEV(probabilities.AwayWin, (double)odds.AwayWin);
            markets.Add(new MarketEvaluation
            {
                Market = "Away Win",
                Probability = probabilities.AwayWin,
                Odds = (double)odds.AwayWin,
                EV = ev,
                Reason = $"Away {probabilities.AwayWin:P1} @ {odds.AwayWin:F2} = EV {ev:P1}"
            });
        }
        
        // Return best EV > threshold
        return markets
            .Where(m => m.EV > MinimumEV)
            .OrderByDescending(m => m.EV)
            .FirstOrDefault();
    }
    
    private double CalculateEV(double probability, double odds) 
        => (probability * odds) - 1.0;
    
    private bool HasValid1X2Odds(MatchOdds odds) => (double)odds.HomeWin >= MinimumOdds && (double)odds.Draw >= MinimumOdds && (double)odds.AwayWin >= MinimumOdds;
    
    private double PoissonProbability(int goals, double lambda)
    {
        if (lambda <= 0) return goals == 0 ? 1.0 : 0.0;
        double logProb = goals * Math.Log(lambda) - lambda - LogFactorial(goals);
        return Math.Exp(logProb);
    }
    
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
/// Result containing all predictions for a match
/// </summary>
public class PredictionResult
{
    public List<MarketPrediction> Predictions { get; set; } = new();
    
    // Model probabilities
    public double HomeWinProbability { get; set; }
    public double DrawProbability { get; set; }
    public double AwayWinProbability { get; set; }
    public double BTTSProbability { get; set; }
    public double Over25Probability { get; set; }
    
    public bool HasPredictions => Predictions.Any();
}

/// <summary>
/// Individual market prediction
/// </summary>
public class MarketPrediction
{
    public string Market { get; set; } = string.Empty;
    public double Probability { get; set; }
    public double ExpectedValue { get; set; }
    public double Odds { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal class MarketEvaluation
{
    public string Market { get; set; } = string.Empty;
    public double Probability { get; set; }
    public double Odds { get; set; }
    public double EV { get; set; }
    public string Reason { get; set; } = string.Empty;
}
