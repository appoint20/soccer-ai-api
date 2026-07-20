using FluentAssertions;
using SoccerAi.Application.Services.Evaluation;

namespace soccer_ai_unit_tests.Services;

public class CalibrationDivergenceTests
{
    [Fact]
    public void RecoversRawModelDivergence_ThroughHalfBlend()
    {
        // p_DC = 0.70, p_mkt = 0.50, w = 0.5 → p_cal = 0.60.
        // |p_cal − p_mkt| = 0.10 understates; recovery: 0.10 / 0.5 = 0.20 = |p_DC − p_mkt| ✓
        CalibrationDivergence.RecoverModelDivergence(0.60, 0.50, 0.5)
            .Should().BeApproximately(0.20, 1e-12);
    }

    [Fact]
    public void ZeroMarketWeight_NoCorrectionNeeded()
    {
        // w = 0 → p_cal IS p_DC
        CalibrationDivergence.RecoverModelDivergence(0.70, 0.50, 0.0)
            .Should().BeApproximately(0.20, 1e-12);
    }

    [Fact]
    public void FullMarketWeight_DegeneratesToRawDifference()
    {
        // w ≈ 1 → p_cal ≈ p_mkt; recovery impossible, return raw
        CalibrationDivergence.RecoverModelDivergence(0.51, 0.50, 1.0)
            .Should().BeApproximately(0.01, 1e-12);
    }

    [Fact]
    public void ResultClampedToProbabilityRange()
    {
        CalibrationDivergence.RecoverModelDivergence(0.99, 0.01, 0.5)
            .Should().BeLessThanOrEqualTo(1.0);
    }
}
