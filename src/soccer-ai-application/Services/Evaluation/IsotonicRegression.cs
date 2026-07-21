namespace SoccerAi.Application.Services.Evaluation;

/// <summary>
/// Isotonic regression via Pool-Adjacent-Violators (PAV).
/// Maps a raw probability to the monotonically-nondecreasing calibrated
/// probability that best fits observed outcomes. Pure and immutable after fit.
/// </summary>
public sealed class IsotonicRegression
{
    /// <summary>Fitted blocks: x-range → calibrated value (nondecreasing).</summary>
    private readonly double[] _blockUpperX;
    private readonly double[] _blockValue;

    private IsotonicRegression(double[] blockUpperX, double[] blockValue)
    {
        _blockUpperX = blockUpperX;
        _blockValue = blockValue;
    }

    public int BlockCount => _blockValue.Length;

    public static IsotonicRegression Fit(IReadOnlyCollection<(double X, bool Y)> samples)
    {
        if (samples.Count == 0)
            return new IsotonicRegression([1.0], [0.5]);

        var ordered = samples.OrderBy(s => s.X).ToList();

        // PAV: blocks of (sum, weight, maxX); merge while means decrease.
        var sums = new List<double>();
        var weights = new List<double>();
        var maxXs = new List<double>();

        foreach (var (x, y) in ordered)
        {
            sums.Add(y ? 1 : 0);
            weights.Add(1);
            maxXs.Add(x);

            while (sums.Count > 1 &&
                   sums[^2] / weights[^2] >= sums[^1] / weights[^1])
            {
                sums[^2] += sums[^1];
                weights[^2] += weights[^1];
                maxXs[^2] = maxXs[^1];
                sums.RemoveAt(sums.Count - 1);
                weights.RemoveAt(weights.Count - 1);
                maxXs.RemoveAt(maxXs.Count - 1);
            }
        }

        var values = sums.Select((s, i) => s / weights[i]).ToArray();
        return new IsotonicRegression(maxXs.ToArray(), values);
    }

    /// <summary>
    /// Calibrated probability for a raw probability (step function over the
    /// fitted blocks; values clamped to (0.01, 0.99) so downstream log loss
    /// and EV never see saturated 0/1).
    /// </summary>
    public double Predict(double x)
    {
        var idx = Array.BinarySearch(_blockUpperX, x);
        if (idx < 0) idx = ~idx;                       // first block with upperX >= x
        if (idx >= _blockValue.Length) idx = _blockValue.Length - 1;
        return Math.Clamp(_blockValue[idx], 0.01, 0.99);
    }
}
