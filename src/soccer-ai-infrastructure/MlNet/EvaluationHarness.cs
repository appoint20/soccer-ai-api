namespace SoccerAi.Infrastructure.MlNet;

/// <summary>One scored prediction on held-out data.</summary>
public sealed record PredictionSample(string Market, int LeagueId, double Probability, bool Outcome);

/// <summary>One calibration bucket: predicted range vs observed hit rate.</summary>
public sealed record CalibrationBucket(
    double Lower, double Upper, int Count, double MeanPredicted, double ObservedRate);

/// <summary>Metrics for one (market, league) slice. League "ALL" aggregates the market.</summary>
public sealed record EvaluationSlice(
    string Market,
    string League,
    int Samples,
    double BrierScore,
    double LogLoss,
    double Accuracy,
    IReadOnlyList<CalibrationBucket> Calibration);

/// <summary>
/// Pure-math evaluation harness: Brier score, log loss, calibration buckets
/// and accuracy — per market and per league. No ML.NET dependency, fully
/// unit-testable.
/// </summary>
public static class EvaluationHarness
{
    private const double Epsilon = 1e-15;
    public const int DefaultCalibrationBuckets = 10;

    public static double Brier(IReadOnlyCollection<PredictionSample> samples) =>
        samples.Count == 0
            ? 0
            : samples.Average(s => Math.Pow(s.Probability - (s.Outcome ? 1.0 : 0.0), 2));

    public static double LogLoss(IReadOnlyCollection<PredictionSample> samples) =>
        samples.Count == 0
            ? 0
            : -samples.Average(s =>
            {
                var p = Math.Clamp(s.Probability, Epsilon, 1 - Epsilon);
                return s.Outcome ? Math.Log(p) : Math.Log(1 - p);
            });

    public static double Accuracy(IReadOnlyCollection<PredictionSample> samples) =>
        samples.Count == 0
            ? 0
            : samples.Average(s => (s.Probability >= 0.5) == s.Outcome ? 1.0 : 0.0);

    public static IReadOnlyList<CalibrationBucket> Calibration(
        IReadOnlyCollection<PredictionSample> samples,
        int buckets = DefaultCalibrationBuckets)
    {
        var result = new List<CalibrationBucket>(buckets);
        var width = 1.0 / buckets;

        for (var i = 0; i < buckets; i++)
        {
            var lower = i * width;
            var upper = (i + 1) * width;

            // Last bucket is inclusive of 1.0
            var inBucket = samples
                .Where(s => s.Probability >= lower &&
                            (i == buckets - 1 ? s.Probability <= upper : s.Probability < upper))
                .ToList();

            result.Add(new CalibrationBucket(
                Math.Round(lower, 4),
                Math.Round(upper, 4),
                inBucket.Count,
                inBucket.Count > 0 ? inBucket.Average(s => s.Probability) : 0,
                inBucket.Count > 0 ? inBucket.Average(s => s.Outcome ? 1.0 : 0.0) : 0));
        }

        return result;
    }

    /// <summary>
    /// Full evaluation: for every market an "ALL" slice plus one slice per league.
    /// </summary>
    public static List<EvaluationSlice> Evaluate(IEnumerable<PredictionSample> allSamples)
    {
        var slices = new List<EvaluationSlice>();

        foreach (var marketGroup in allSamples.GroupBy(s => s.Market).OrderBy(g => g.Key))
        {
            var marketSamples = marketGroup.ToList();
            slices.Add(BuildSlice(marketGroup.Key, "ALL", marketSamples));

            foreach (var leagueGroup in marketSamples.GroupBy(s => s.LeagueId).OrderBy(g => g.Key))
                slices.Add(BuildSlice(marketGroup.Key, leagueGroup.Key.ToString(), leagueGroup.ToList()));
        }

        return slices;
    }

    private static EvaluationSlice BuildSlice(string market, string league, IReadOnlyCollection<PredictionSample> samples) =>
        new(market,
            league,
            samples.Count,
            Math.Round(Brier(samples), 6),
            Math.Round(LogLoss(samples), 6),
            Math.Round(Accuracy(samples), 6),
            Calibration(samples));
}
