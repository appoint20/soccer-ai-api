using FluentAssertions;
using SoccerAi.Application.Services.Combinations;

namespace soccer_ai_unit_tests.Services;

public class BacktestCombinationBuilderTests
{
    private static BacktestPick Pick(int fixtureId, double prob = 0.65, double odds = 1.8,
        string market = "Over 2.5 Goals") =>
        new(fixtureId, "Premier League", $"Home{fixtureId}", $"Away{fixtureId}", market, prob, odds);

    [Fact]
    public void FewerThanTwoPicks_NoCombinations()
    {
        BacktestCombinationBuilder.Build([Pick(1)], 1.5).Should().BeEmpty();
        BacktestCombinationBuilder.Build([], 1.5).Should().BeEmpty();
    }

    [Fact]
    public void TwoPicks_OneDouble_WithProductOdds()
    {
        var combos = BacktestCombinationBuilder.Build([Pick(1, odds: 2.0), Pick(2, odds: 1.5)], 1.5);

        combos.Should().ContainSingle();
        combos[0].Matches.Should().HaveCount(2);
        combos[0].TotalOdds.Should().Be(3.0);
        combos[0].SourceType.Should().Be("DETERMINISTIC");
    }

    [Fact]
    public void OddsBelowMinimum_AreExcluded()
    {
        var combos = BacktestCombinationBuilder.Build(
            [Pick(1, odds: 1.2), Pick(2, odds: 1.3)], minSelectionOdds: 1.5);

        combos.Should().BeEmpty("both picks are under the odds floor");
    }

    [Fact]
    public void SameFixtureTwice_OnlyBestPickUsed()
    {
        var combos = BacktestCombinationBuilder.Build(
            [Pick(1, prob: 0.60, market: "BTTS"), Pick(1, prob: 0.70, market: "Over 2.5 Goals"), Pick(2)],
            1.5);

        combos.Should().NotBeEmpty();
        foreach (var combo in combos)
            combo.Matches.Select(m => m.FixtureId).Should().OnlyHaveUniqueItems();

        // Fixture 1 must be represented by its higher-probability pick
        combos.SelectMany(c => c.Matches)
            .Where(m => m.FixtureId == 1)
            .Should().OnlyContain(m => m.Selection == "Over 2.5 Goals");
    }

    [Fact]
    public void ManyPicks_CapsAtFiveCombos_RankedByAvgProbability()
    {
        var picks = Enumerable.Range(1, 8).Select(i => Pick(i, prob: 0.5 + i * 0.05)).ToList();

        var combos = BacktestCombinationBuilder.Build(picks, 1.5);

        combos.Should().HaveCount(BacktestCombinationBuilder.MaxCombinationsPerDay);
        combos.Should().OnlyContain(c =>
            c.Matches.Count >= BacktestCombinationBuilder.MinLegs &&
            c.Matches.Count <= BacktestCombinationBuilder.MaxLegs);

        // Best combo should contain the highest-probability picks
        var avg = combos.Select(c => c.Matches.Average(m => m.Confidence)).ToList();
        avg.Should().BeInDescendingOrder();
    }
}

public class EvaluationHarnessExtensionsTests
{
    private static SoccerAi.Application.Services.Evaluation.PredictionSample S(double p, bool y) =>
        new("m", 0, p, y);

    [Fact]
    public void CalibrationForRanges_UsesProductBuckets()
    {
        var samples = new[] { S(0.52, true), S(0.57, false), S(0.63, true), S(0.80, true), S(0.40, false) };

        var buckets = SoccerAi.Application.Services.Evaluation.EvaluationHarness
            .CalibrationForRanges(samples, [(0.50, 0.55), (0.55, 0.60), (0.60, 0.65), (0.65, 1.00)]);

        buckets.Should().HaveCount(4);
        buckets[0].Count.Should().Be(1); // 0.52
        buckets[1].Count.Should().Be(1); // 0.57
        buckets[2].Count.Should().Be(1); // 0.63
        buckets[3].Count.Should().Be(1); // 0.80 (last bucket inclusive)
        buckets.Sum(b => b.Count).Should().Be(4, "0.40 falls outside all ranges");
    }

    [Fact]
    public void MulticlassBrier_KnownValues()
    {
        // Perfect: p=[1,0,0], actual 0 → 0. Uniform: [1/3,1/3,1/3] any actual → (2/3)²+2(1/3)² = 0.6667
        var perfect = new[] { (new[] { 1.0, 0.0, 0.0 }, 0) };
        var uniform = new[] { (new[] { 1 / 3.0, 1 / 3.0, 1 / 3.0 }, 1) };

        SoccerAi.Application.Services.Evaluation.EvaluationHarness.MulticlassBrier(perfect).Should().Be(0);
        SoccerAi.Application.Services.Evaluation.EvaluationHarness.MulticlassBrier(uniform)
            .Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void MulticlassLogLoss_KnownValues()
    {
        // -ln(0.5) ≈ 0.6931
        var samples = new[] { (new[] { 0.5, 0.3, 0.2 }, 0) };

        SoccerAi.Application.Services.Evaluation.EvaluationHarness.MulticlassLogLoss(samples)
            .Should().BeApproximately(0.693147, 1e-5);
    }

    [Fact]
    public void MulticlassLogLoss_ClampsZeroProbability()
    {
        var samples = new[] { (new[] { 0.0, 0.5, 0.5 }, 0) };

        double.IsFinite(SoccerAi.Application.Services.Evaluation.EvaluationHarness.MulticlassLogLoss(samples))
            .Should().BeTrue();
    }
}
