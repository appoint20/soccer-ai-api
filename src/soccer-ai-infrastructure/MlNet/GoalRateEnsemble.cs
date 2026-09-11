using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>One convex weight for complete score distributions, learned on calibration rows only.</summary>
public static class GoalRateEnsemble
{
    public const string Recipe = "dc-ml-score-mixture-v1";

    public static double FitWeight(IEnumerable<(MatrixMarkets Ml, MatrixMarkets Dc, bool Over, bool Btts)> samples)
    {
        double numerator = 0, denominator = 0; var count = 0;
        foreach (var (ml, dc, over, btts) in samples)
        {
            count++;
            foreach (var (candidate, baseline, actual) in new[] {
                (ml.Over25, dc.Over25, over), (ml.Btts, dc.Btts, btts) })
            {
                var difference = candidate - baseline;
                numerator += difference * ((actual ? 1 : 0) - baseline);
                denominator += difference * difference;
            }
        }
        // Identical or unsupported models do not justify extra learned influence.
        return count < 100 || denominator < 1e-12 ? 0 : Math.Clamp(numerator / denominator, 0, 1);
    }

    public static MatrixMarkets Mix(MatrixMarkets ml, MatrixMarkets dc, double mlWeight)
    {
        if (!double.IsFinite(mlWeight) || mlWeight is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(mlWeight));
        double M(double learned, double baseline) => mlWeight * learned + (1 - mlWeight) * baseline;
        return new(M(ml.HomeWin, dc.HomeWin), M(ml.Draw, dc.Draw), M(ml.AwayWin, dc.AwayWin),
            M(ml.Over25, dc.Over25), M(ml.Btts, dc.Btts), M(ml.TwoToThreeGoals, dc.TwoToThreeGoals),
            M(ml.BttsAndOver25, dc.BttsAndOver25));
    }
}
