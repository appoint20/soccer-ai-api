using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

/// <summary>
/// Builds analytical signals from models - all normalized 0-1
/// </summary>
public sealed class SignalBuilderService : ISignalBuilderService
{
    // Normalization thresholds
    private const double DrawMinThreshold = 0.15;
    private const double DrawMaxThreshold = 0.40;
    private const double BTTSMinThreshold = 0.40;
    private const double BTTSMaxThreshold = 0.70;
    private const double Over25MinThreshold = 0.45;
    private const double Over25MaxThreshold = 0.75;
    private const double GoalBalanceThreshold = 0.35;
    private const double ScoringDrawMinThreshold = 0.05;
    private const double ScoringDrawMaxThreshold = 0.20;
    
    // Weights for combined signals
    private const double PoissonWeight = 0.40;
    private const double MonteCarloWeight = 0.35;
    private const double H2HWeight = 0.25;

    public AnalyticalSignals Build(PoissonModel poisson, MonteCarloModel monteCarlo, HeadToHeadModel h2h)
    {
        // Draw signals
        var poissonDrawSignal = Normalize(poisson.Draw, DrawMinThreshold, DrawMaxThreshold);
        var mcDrawSignal = Normalize(monteCarlo.Draw, DrawMinThreshold, DrawMaxThreshold);
        var goalBalanceSignal = CalculateGoalBalanceSignal(poisson.ExpectedScoreDifference);
        var scoringDrawSignal = Normalize(poisson.ScoringDrawProb, ScoringDrawMinThreshold, ScoringDrawMaxThreshold);
        var h2hDrawSignal = h2h.IsValid ? Normalize(h2h.DrawRate, 0.20, 0.50) : 0.5;
        
        // BTTS signals
        var poissonBTTSSignal = Normalize(poisson.BTTS, BTTSMinThreshold, BTTSMaxThreshold);
        var mcBTTSSignal = Normalize(monteCarlo.BTTS, BTTSMinThreshold, BTTSMaxThreshold);
        var h2hBTTSSignal = h2h.IsValid ? Normalize(h2h.BTTSRate, BTTSMinThreshold, BTTSMaxThreshold) : 0.5;
        
        // Over 2.5 signals
        var poissonOver25Signal = Normalize(poisson.Over25, Over25MinThreshold, Over25MaxThreshold);
        var mcOver25Signal = Normalize(monteCarlo.Over25, Over25MinThreshold, Over25MaxThreshold);
        var h2hOver25Signal = h2h.IsValid ? Normalize(h2h.Over25Rate, Over25MinThreshold, Over25MaxThreshold) : 0.5;
        
        // Combined signals (weighted average)
        var drawCombined = CombineSignals(poissonDrawSignal, mcDrawSignal, h2hDrawSignal, goalBalanceSignal, scoringDrawSignal);
        var bttsCombined = CombineThreeSignals(poissonBTTSSignal, mcBTTSSignal, h2hBTTSSignal);
        var over25Combined = CombineThreeSignals(poissonOver25Signal, mcOver25Signal, h2hOver25Signal);

        return new AnalyticalSignals
        {
            // Draw signals
            PoissonDrawSignal = Round(poissonDrawSignal),
            MonteCarloDrawSignal = Round(mcDrawSignal),
            GoalBalanceSignal = Round(goalBalanceSignal),
            ScoringDrawProfileSignal = Round(scoringDrawSignal),
            H2HDrawSignal = Round(h2hDrawSignal),
            LambdaDifference = Round(poisson.ExpectedScoreDifference),
            HighScoringDrawProbability = Round(poisson.ScoringDrawProb),
            
            // BTTS signals
            PoissonBTTSSignal = Round(poissonBTTSSignal),
            MonteCarloBTTSSignal = Round(mcBTTSSignal),
            H2HBTTSSignal = Round(h2hBTTSSignal),
            
            // Over 2.5 signals
            PoissonOver25Signal = Round(poissonOver25Signal),
            MonteCarloOver25Signal = Round(mcOver25Signal),
            H2HOver25Signal = Round(h2hOver25Signal),
            
            // Combined
            DrawCombinedSignal = Round(drawCombined),
            BTTSCombinedSignal = Round(bttsCombined),
            Over25CombinedSignal = Round(over25Combined)
        };
    }

    private static double CalculateGoalBalanceSignal(double lambdaDiff)
    {
        var absDiff = Math.Abs(lambdaDiff);
        if (absDiff <= GoalBalanceThreshold) return 1.0;
        return Math.Max(0, 1 - ((absDiff - GoalBalanceThreshold) / 1.0));
    }

    private static double CombineSignals(double poisson, double mc, double h2h, double balance, double scoringDraw)
    {
        // Draw uses 5 signals with custom weights
        return (poisson * 0.30) + (mc * 0.25) + (h2h * 0.15) + (balance * 0.15) + (scoringDraw * 0.15);
    }

    private static double CombineThreeSignals(double poisson, double mc, double h2h)
    {
        return (poisson * PoissonWeight) + (mc * MonteCarloWeight) + (h2h * H2HWeight);
    }

    private static double Normalize(double value, double min, double max)
    {
        if (value <= min) return 0;
        if (value >= max) return 1;
        return (value - min) / (max - min);
    }

    private static double Round(double value) => Math.Round(value, 3);
}
