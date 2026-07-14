using FluentAssertions;
using SoccerAi.Infrastructure.MlNet;

namespace soccer_ai_unit_tests.MlNet;

public class EvaluationHarnessTests
{
    private static PredictionSample S(double p, bool y, string market = "over25", int league = 39) =>
        new(market, league, p, y);

    // ── Brier score ──────────────────────────────────────────────────────────

    [Fact]
    public void Brier_PerfectPredictions_IsZero()
    {
        var samples = new[] { S(1.0, true), S(0.0, false) };
        EvaluationHarness.Brier(samples).Should().Be(0);
    }

    [Fact]
    public void Brier_WorstPredictions_IsOne()
    {
        var samples = new[] { S(0.0, true), S(1.0, false) };
        EvaluationHarness.Brier(samples).Should().Be(1);
    }

    [Fact]
    public void Brier_KnownValue()
    {
        // (0.8−1)² = 0.04, (0.3−0)² = 0.09 → mean 0.065
        var samples = new[] { S(0.8, true), S(0.3, false) };
        EvaluationHarness.Brier(samples).Should().BeApproximately(0.065, 1e-12);
    }

    // ── Log loss ─────────────────────────────────────────────────────────────

    [Fact]
    public void LogLoss_KnownValue()
    {
        // −[ln(0.8) + ln(0.7)] / 2 = (0.22314 + 0.35667) / 2 = 0.28991
        var samples = new[] { S(0.8, true), S(0.3, false) };
        EvaluationHarness.LogLoss(samples).Should().BeApproximately(0.289907, 1e-5);
    }

    [Fact]
    public void LogLoss_ClampsExtremeProbabilities_NoInfinity()
    {
        var samples = new[] { S(0.0, true), S(1.0, false) };
        var loss = EvaluationHarness.LogLoss(samples);
        double.IsFinite(loss).Should().BeTrue("probabilities are clamped away from 0/1");
        loss.Should().BeGreaterThan(10, "confidently wrong predictions are punished hard");
    }

    // ── Accuracy ─────────────────────────────────────────────────────────────

    [Fact]
    public void Accuracy_ThresholdAtHalf()
    {
        var samples = new[]
        {
            S(0.7, true),   // correct
            S(0.4, false),  // correct
            S(0.6, false),  // wrong
            S(0.2, true)    // wrong
        };
        EvaluationHarness.Accuracy(samples).Should().Be(0.5);
    }

    // ── Calibration buckets ──────────────────────────────────────────────────

    [Fact]
    public void Calibration_BucketsCoverFullRange_AndCountsAddUp()
    {
        var samples = Enumerable.Range(0, 100)
            .Select(i => S(i / 100.0, i % 2 == 0))
            .ToList();

        var buckets = EvaluationHarness.Calibration(samples);

        buckets.Should().HaveCount(EvaluationHarness.DefaultCalibrationBuckets);
        buckets.Sum(b => b.Count).Should().Be(100, "every sample lands in exactly one bucket");
        buckets[0].Lower.Should().Be(0);
        buckets[^1].Upper.Should().Be(1);
    }

    [Fact]
    public void Calibration_ProbabilityOfOne_LandsInLastBucket()
    {
        var buckets = EvaluationHarness.Calibration([S(1.0, true)]);
        buckets[^1].Count.Should().Be(1);
    }

    [Fact]
    public void Calibration_PerfectlyCalibratedBucket_ObservedMatchesPredicted()
    {
        // 10 samples at p=0.75, 7-8 hits → observed ≈ predicted for calibrated model
        var samples = Enumerable.Range(0, 100)
            .Select(i => S(0.75, i < 75))
            .ToList();

        var buckets = EvaluationHarness.Calibration(samples);
        var bucket = buckets.Single(b => b.Count > 0);

        bucket.MeanPredicted.Should().BeApproximately(0.75, 1e-9);
        bucket.ObservedRate.Should().BeApproximately(0.75, 1e-9);
    }

    // ── Per-market / per-league slicing ──────────────────────────────────────

    [Fact]
    public void Evaluate_ProducesAllSlicePlusPerLeagueSlices()
    {
        var samples = new[]
        {
            S(0.8, true, "over25", 39),
            S(0.6, false, "over25", 39),
            S(0.7, true, "over25", 78),
            S(0.5, true, "btts", 39)
        };

        var slices = EvaluationHarness.Evaluate(samples);

        slices.Should().Contain(s => s.Market == "over25" && s.League == "ALL" && s.Samples == 3);
        slices.Should().Contain(s => s.Market == "over25" && s.League == "39" && s.Samples == 2);
        slices.Should().Contain(s => s.Market == "over25" && s.League == "78" && s.Samples == 1);
        slices.Should().Contain(s => s.Market == "btts" && s.League == "ALL" && s.Samples == 1);
    }

    [Fact]
    public void Evaluate_EmptyInput_ReturnsNoSlices()
    {
        EvaluationHarness.Evaluate([]).Should().BeEmpty();
    }
}
