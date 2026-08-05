namespace SoccerAi.Application.Services.Evaluation;

/// <summary>
/// Isotonic regression via Pool-Adjacent-Violators (PAV).
/// Maps a raw probability to the monotonically-nondecreasing calibrated
/// probability that best fits observed outcomes. Pure and immutable after fit.
///
/// Small-sample shrinkage: a PAV block built from few observations is noisy —
/// baseline v6 showed the 65%+ winner block (n=42) claiming 83% and then
/// delivering 69% out of sample. Each block's correction is therefore shrunk
/// toward the raw probability by weight w = n / (n + PriorStrength), so thin
/// blocks barely move the input and dense blocks move it fully.
/// </summary>
public sealed class IsotonicRegression
{
    /// <summary>Pseudo-observations of "trust the raw probability" added to every block.</summary>
    public const double DefaultPriorStrength = 50;

    private readonly double[] _blockUpperX;
    private readonly double[] _blockValue;
    private readonly double[] _blockWeight;
    private readonly double _priorStrength;

    private IsotonicRegression(
        double[] blockUpperX, double[] blockValue, double[] blockWeight, double priorStrength)
    {
        _blockUpperX = blockUpperX;
        _blockValue = blockValue;
        _blockWeight = blockWeight;
        _priorStrength = priorStrength;
    }

    public int BlockCount => _blockValue.Length;

    public static IsotonicRegression Fit(
        IReadOnlyCollection<(double X, bool Y)> samples,
        double priorStrength = DefaultPriorStrength)
    {
        if (samples.Count == 0)
            return new IsotonicRegression([1.0], [0.5], [0], priorStrength);

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
        return new IsotonicRegression(
            maxXs.ToArray(), values, weights.ToArray(), priorStrength);
    }

    /// <summary>
    /// Calibrated probability for a raw probability. The fitted block value is
    /// blended with the raw input according to the block's sample count, then
    /// clamped to (0.01, 0.99) so log loss and EV never see saturated 0/1.
    /// </summary>
    public double Predict(double x)
    {
        var idx = Array.BinarySearch(_blockUpperX, x);
        if (idx < 0) idx = ~idx;                       // first block with upperX >= x
        if (idx >= _blockValue.Length) idx = _blockValue.Length - 1;

        var n = _blockWeight[idx];
        var trust = n / (n + _priorStrength);          // 0 = keep raw, 1 = full correction
        var calibrated = trust * _blockValue[idx] + (1 - trust) * x;

        return Math.Clamp(calibrated, 0.01, 0.99);
    }

    /// <summary>Sample count backing the block that covers this probability (diagnostics).</summary>
    public double BlockSampleCount(double x)
    {
        var idx = Array.BinarySearch(_blockUpperX, x);
        if (idx < 0) idx = ~idx;
        if (idx >= _blockWeight.Length) idx = _blockWeight.Length - 1;
        return _blockWeight[idx];
    }
}
