namespace SoccerAi.Infrastructure.MlNet;

public sealed record PairedBrierInterval(int Samples, int WeekClusters, double Difference, double Lower95, double Upper95);
public sealed record WinnerEvaluation(int Samples, double Accuracy, double BrierScore, double LogLoss);

public static class PairedModelMetrics
{
    /// <summary>Exploratory uncertainty, resampling whole UTC weeks across leagues.</summary>
    public static PairedBrierInterval Brier(IEnumerable<(DateTime Date, double Candidate, double Baseline, bool Actual)> rows)
    {
        var blocks = rows.GroupBy(r => r.Date.Date.AddDays(-((int)r.Date.DayOfWeek + 6) % 7))
            .Select(g => (N: g.Count(), Sum: g.Sum(r => Math.Pow(r.Candidate - (r.Actual ? 1 : 0), 2) - Math.Pow(r.Baseline - (r.Actual ? 1 : 0), 2))))
            .ToArray();
        if (blocks.Length == 0) return new(0, 0, 0, 0, 0);
        var rng = new Random(20260911); var samples = new double[2000];
        for (var i = 0; i < samples.Length; i++)
        {
            double sum = 0; int n = 0;
            for (var j = 0; j < blocks.Length; j++) { var b = blocks[rng.Next(blocks.Length)]; sum += b.Sum; n += b.N; }
            samples[i] = sum / n;
        }
        Array.Sort(samples);
        return new(blocks.Sum(b => b.N), blocks.Length, blocks.Sum(b => b.Sum) / blocks.Sum(b => b.N), samples[49], samples[1949]);
    }

    public static WinnerEvaluation Winner(IEnumerable<(double Home, double Draw, double Away, int Actual)> rows)
    {
        var data = rows.ToList();
        if (data.Count == 0) return new(0, 0, 0, 0);
        int Prediction((double Home, double Draw, double Away, int Actual) r) => r.Draw >= r.Home && r.Draw >= r.Away ? 1 : r.Away > r.Home ? 2 : 0;
        double P((double Home, double Draw, double Away, int Actual) r, int i) => i == 0 ? r.Home : i == 1 ? r.Draw : r.Away;
        return new(data.Count, Math.Round(100d * data.Count(r => Prediction(r) == r.Actual) / data.Count, 2),
            Math.Round(data.Average(r => Enumerable.Range(0, 3).Sum(i => Math.Pow(P(r, i) - (r.Actual == i ? 1 : 0), 2))), 6),
            Math.Round(data.Average(r => -Math.Log(Math.Clamp(P(r, r.Actual), 1e-6, 1 - 1e-6))), 6));
    }
}
