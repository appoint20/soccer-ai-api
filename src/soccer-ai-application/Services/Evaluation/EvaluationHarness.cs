namespace SoccerAi.Application.Services.Evaluation;

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

    /// <summary>
    /// Calibration over custom bucket boundaries, e.g. the product buckets
    /// [0.50–0.55), [0.55–0.60), [0.60–0.65), [0.65–1.0]. The last bucket is
    /// upper-inclusive. Samples outside all ranges are ignored.
    /// </summary>
    public static IReadOnlyList<CalibrationBucket> CalibrationForRanges(
        IReadOnlyCollection<PredictionSample> samples,
        IReadOnlyList<(double Lower, double Upper)> ranges)
    {
        var result = new List<CalibrationBucket>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            var (lower, upper) = ranges[i];
            var last = i == ranges.Count - 1;
            var inBucket = samples
                .Where(s => s.Probability >= lower &&
                            (last ? s.Probability <= upper : s.Probability < upper))
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

    /// <summary>Multiclass Brier: mean over samples of Σ (p_i − y_i)².</summary>
    public static double MulticlassBrier(IReadOnlyCollection<(double[] Probabilities, int ActualIndex)> samples) =>
        samples.Count == 0
            ? 0
            : samples.Average(s => s.Probabilities
                .Select((p, i) => Math.Pow(p - (i == s.ActualIndex ? 1.0 : 0.0), 2))
                .Sum());

    /// <summary>Multiclass log loss: −mean ln p(actual outcome).</summary>
    public static double MulticlassLogLoss(IReadOnlyCollection<(double[] Probabilities, int ActualIndex)> samples) =>
        samples.Count == 0
            ? 0
            : -samples.Average(s =>
                Math.Log(Math.Clamp(s.Probabilities[s.ActualIndex], Epsilon, 1 - Epsilon)));

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
