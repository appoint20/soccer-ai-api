using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class MarketCalibratorTests
{
    private static MarketCalibrator CreateSut(double marketWeight = 0.5) =>
        new(Microsoft.Extensions.Options.Options.Create(
            new CalibrationOptions { MarketWeight = marketWeight }));

    private static PoissonProbabilities ModelProbs() => new()
    {
        HomeWin = 0.50,
        Draw = 0.28,
        AwayWin = 0.22,
        Over25 = 0.60,
        BothTeamScoredGoal = 0.55,
        TwoToThreeGoals = 0.40,
        HomeExpectedGoals = 1.8,
        AwayExpectedGoals = 1.1
    };

    [Fact]
    public void WithoutAnyOdds_ReturnsPureModelProbabilities()
    {
        var result = CreateSut().Calibrate(ModelProbs(), new Fixture());

        result.HomeWin.Should().Be(0.50);
        result.Draw.Should().Be(0.28);
        result.AwayWin.Should().Be(0.22);
        result.Over25.Should().Be(0.60);
        result.Btts.Should().Be(0.55);
        result.TwoToThreeGoals.Should().Be(0.40);
        result.UsedMarketOdds.Should().BeFalse();
    }

    [Fact]
    public void MarketWeightZero_IgnoresOddsCompletely()
    {
        var fixture = new Fixture
        {
            HomeWinOdds = 1.5, DrawOdds = 4.0, AwayWinOdds = 7.0,
            Over25Odds = 1.6, Under25Odds = 2.3, BttsYesOdds = 1.7
        };

        var result = CreateSut(marketWeight: 0).Calibrate(ModelProbs(), fixture);

        // 1X2 renormalization of pure model probs must not distort them
        result.HomeWin.Should().BeApproximately(0.50, 1e-9);
        result.Over25.Should().BeApproximately(0.60, 1e-9);
        result.Btts.Should().BeApproximately(0.55, 1e-9);
    }

    [Fact]
    public void MarketWeightOne_SymmetricOverUnderOdds_GivesFiftyPercent()
    {
        var fixture = new Fixture { Over25Odds = 2.0, Under25Odds = 2.0 };

        var result = CreateSut(marketWeight: 1.0).Calibrate(ModelProbs(), fixture);

        result.Over25.Should().BeApproximately(0.5, 1e-6,
            "symmetric odds imply 50% after margin removal");
        result.UsedMarketOdds.Should().BeTrue();
    }

    [Fact]
    public void DefaultWeight_BlendsHalfModelHalfMarket()
    {
        // Margin-free two-way odds: over 1/0.4=2.5, under 1/0.6≈1.6667
        var fixture = new Fixture { Over25Odds = 2.5, Under25Odds = 1.0 / 0.6 };

        var result = CreateSut(marketWeight: 0.5).Calibrate(ModelProbs(), fixture);

        // final = 0.5 × 0.60 (model) + 0.5 × 0.40 (market) = 0.50
        result.Over25.Should().BeApproximately(0.50, 1e-3);
    }

    [Fact]
    public void OneXTwo_WithOdds_StaysAProperDistribution()
    {
        var fixture = new Fixture { HomeWinOdds = 2.1, DrawOdds = 3.4, AwayWinOdds = 3.6 };

        var result = CreateSut().Calibrate(ModelProbs(), fixture);

        (result.HomeWin + result.Draw + result.AwayWin)
            .Should().BeApproximately(1.0, 1e-9, "1X2 is renormalized after blending");
        result.HomeWin.Should().BeGreaterThan(result.AwayWin,
            "both model and market favour the home side");
        result.UsedMarketOdds.Should().BeTrue();
    }

    [Fact]
    public void Btts_UsesNaiveImpliedWhenOnlyYesOddsExist()
    {
        var fixture = new Fixture { BttsYesOdds = 2.0 };

        var result = CreateSut(marketWeight: 1.0).Calibrate(ModelProbs(), fixture);

        result.Btts.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void TwoToThreeGoals_AlwaysPureModel_NoOddsMarketExists()
    {
        var fixture = new Fixture
        {
            HomeWinOdds = 2.0, DrawOdds = 3.5, AwayWinOdds = 4.0,
            Over25Odds = 1.9, Under25Odds = 1.9, BttsYesOdds = 1.8
        };

        var result = CreateSut(marketWeight: 1.0).Calibrate(ModelProbs(), fixture);

        result.TwoToThreeGoals.Should().Be(0.40);
    }

    [Fact]
    public void InvalidOdds_AreTreatedAsMissing()
    {
        var fixture = new Fixture { Over25Odds = 1.0, Under25Odds = 0.5, BttsYesOdds = -2 };

        var result = CreateSut().Calibrate(ModelProbs(), fixture);

        result.Over25.Should().Be(0.60);
        result.Btts.Should().Be(0.55);
        result.UsedMarketOdds.Should().BeFalse();
    }
}

public class ShinMarginRemovalTests
{
    [Fact]
    public void TwoWay_SymmetricOdds_GiveHalfEach()
    {
        ShinMarginRemoval.TrueProbability(2.0, 2.0).Should().BeApproximately(0.5, 1e-9);
        // With bookmaker margin (1.8/1.8 implies 111%), still 50/50 after removal
        ShinMarginRemoval.TrueProbability(1.8, 1.8).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void TwoWay_AsymmetricOdds_RemoveMarginProportionally()
    {
        // inv = 0.6667 / 0.4, sum = 1.0667 → 0.625 / 0.375
        var p = ShinMarginRemoval.TrueProbability(1.5, 2.5);
        p.Should().BeApproximately(0.625, 1e-9);
    }

    [Fact]
    public void ThreeWay_ProbabilitiesSumToApproximatelyOne()
    {
        var probs = ShinMarginRemoval.TrueProbabilities([2.5, 3.2, 3.0]);

        probs.Sum().Should().BeApproximately(1.0, 0.02);
        probs[0].Should().BeGreaterThan(probs[1]);
        probs[0].Should().BeGreaterThan(probs[2]);
    }

    [Fact]
    public void InvalidOdds_ReturnZeros()
    {
        var probs = ShinMarginRemoval.TrueProbabilities([2.0, 0, 3.0]);
        probs.Should().OnlyContain(p => p == 0);
    }
}

public class WeightedPredictionFactoryTests
{
    [Fact]
    public void FromCalibrated_MapsProbabilitiesOneToOne()
    {
        var calibrated = new SoccerAi.Application.Interfaces.CalibratedProbabilities
        {
            HomeWin = 0.30, Draw = 0.25, AwayWin = 0.45,
            Over25 = 0.62, Btts = 0.48, TwoToThreeGoals = 0.51
        };

        var prediction = WeightedPrediction.FromCalibrated(calibrated);

        prediction.MatchWinner.Should().Be("away", "away has the higher win probability");
        prediction.Confidence.Should().BeApproximately(0.45, 1e-9);
        prediction.Over25.Should().BeTrue("0.62 > 0.50");
        prediction.Over25Prob.Should().Be(0.62);
        prediction.BTTS.Should().BeFalse("0.48 < 0.50");
        prediction.BTTSProb.Should().Be(0.48);
        prediction.TwoToThreeGoals.Should().BeTrue("0.51 > 0.50");
        prediction.HomeProb.Should().Be(0.30);
        prediction.DrawProb.Should().Be(0.25);
        prediction.AwayProb.Should().Be(0.45);
    }

    [Fact]
    public void FromCalibrated_HomeFavourite_PicksHome()
    {
        var prediction = WeightedPrediction.FromCalibrated(new SoccerAi.Application.Interfaces.CalibratedProbabilities
        {
            HomeWin = 0.55, Draw = 0.25, AwayWin = 0.20,
            Over25 = 0.5, Btts = 0.5, TwoToThreeGoals = 0.5
        });

        prediction.MatchWinner.Should().Be("home");
        prediction.Confidence.Should().BeApproximately(0.55, 1e-9);
    }
}
