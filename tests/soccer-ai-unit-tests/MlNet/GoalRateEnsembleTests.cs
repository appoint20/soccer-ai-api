using FluentAssertions;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.MlNet;

namespace soccer_ai_unit_tests.MlNet;

public class GoalRateEnsembleTests
{
    private static MatrixMarkets Markets(double p) => new(p, (1 - p) / 2, (1 - p) / 2, p, p, .3, p / 2);

    [Fact]
    public void CalibrationWeightFindsKnownOptimumAndRespectsEndpoints()
    {
        var rows = Enumerable.Range(0, 100).Select(i => (Markets(.8), Markets(.4), i < 60, i < 60)).ToArray();
        GoalRateEnsemble.FitWeight(rows).Should().BeApproximately(.5, 1e-12);
        GoalRateEnsemble.FitWeight(rows.Select(r => (r.Item1, r.Item2, true, true))).Should().Be(1);
        GoalRateEnsemble.FitWeight(rows.Select(r => (r.Item1, r.Item2, false, false))).Should().Be(0);
        GoalRateEnsemble.FitWeight(rows.Take(99)).Should().Be(0);
        GoalRateEnsemble.FitWeight(rows.Select(r => (r.Item1, r.Item1, true, true))).Should().Be(0);
    }

    [Fact]
    public void MixturePreservesExclusiveAndJointProbabilityConstraints()
    {
        var ml = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(2.5, 1.8, -.1, 10));
        var dc = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(.8, .5, -.1, 10));
        var mixture = GoalRateEnsemble.Mix(ml, dc, .35);
        (mixture.HomeWin + mixture.Draw + mixture.AwayWin).Should().BeApproximately(1, 1e-10);
        mixture.BttsAndOver25.Should().BeLessThanOrEqualTo(Math.Min(mixture.Btts, mixture.Over25));
        mixture.BttsAndOver25.Should().BeGreaterThanOrEqualTo(Math.Max(0, mixture.Btts + mixture.Over25 - 1));
        GoalRateEnsemble.Mix(ml, dc, 0).Should().Be(dc);
        GoalRateEnsemble.Mix(ml, dc, 1).Should().Be(ml);
        var invalid = () => GoalRateEnsemble.Mix(ml, dc, double.NaN);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UncertaintyResamplesWholeWeeksAndPairsSameFixtures()
    {
        var monday = new DateTime(2026, 8, 31);
        var rows = new[] { monday, monday.AddDays(6), monday.AddDays(7) }
            .Select(d => (d, .6, .6, true));
        var result = PairedModelMetrics.Brier(rows);
        result.Samples.Should().Be(3); result.WeekClusters.Should().Be(2);
        result.Difference.Should().Be(0); result.Lower95.Should().Be(0); result.Upper95.Should().Be(0);
    }

    [Fact]
    public void WinnerUsesThreeClassesAndPenalizesConfidentErrors()
    {
        var result = PairedModelMetrics.Winner([(.7, .2, .1, 0), (.2, .3, .5, 1)]);
        result.Accuracy.Should().Be(50);
        result.BrierScore.Should().BeApproximately(.46, 1e-6);
        result.LogLoss.Should().BeApproximately(( -Math.Log(.7) - Math.Log(.3)) / 2, 1e-6);
    }
}
