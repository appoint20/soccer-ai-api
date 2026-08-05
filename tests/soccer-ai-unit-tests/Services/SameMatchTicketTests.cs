using FluentAssertions;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Application.Services.Evaluation;

namespace soccer_ai_unit_tests.Services;

public class SameMatchTicketTests
{
    private static readonly StrategyOptions Strat = new();
    private static readonly ConfluenceOptions Opt = new();

    [Fact]
    public void JointProbability_IsHigherThanTheProductOfTheTwoMarkets()
    {
        // BTTS and Over 2.5 are positively correlated — multiplying them
        // understates the true chance of the same-match double.
        var matrix = DixonColesMath.BuildScoreMatrix(1.6, 1.3, -0.13, 8);
        var m = DixonColesMath.ComputeMarkets(matrix);

        m.BttsAndOver25.Should().BeGreaterThan(m.Btts * m.Over25,
            "correlation means the joint is above the independent product");
        m.BttsAndOver25.Should().BeLessThanOrEqualTo(Math.Min(m.Btts, m.Over25),
            "a joint can never exceed either component");
    }

    [Fact]
    public void SameMatchPair_RescuesSubFloorMatch_WhenPriceReaches185()
    {
        // Both legs below the 1.70 single floor, product 1.55×1.35 = 2.09 ≥ 1.85
        var pair = new SameMatchPair(1, "Premier League", JointProbability: 0.55,
            BttsOdds: 1.55, Over25Odds: 1.35);

        var tickets = TicketBuilder.Build([], [], Strat, Opt, [pair]);

        tickets.Should().ContainSingle();
        var t = tickets[0];
        t.IsSameMatchPair.Should().BeTrue();
        t.TotalOdds.Should().BeApproximately(2.09, 0.01);
        t.CombinedProbability.Should().Be(0.55, "the TRUE joint, not 0.55 × 0.55");
        t.Ev.Should().BeApproximately(0.55 * 2.0925 - 1, 0.01);
    }

    [Fact]
    public void SameMatchPair_BelowMinimumPrice_Rejected()
    {
        // 1.30 × 1.35 = 1.755 < 1.85
        var pair = new SameMatchPair(1, "Premier League", 0.60, 1.30, 1.35);

        TicketBuilder.Build([], [], Strat, Opt, [pair])
            .Should().BeEmpty("same-match pairs must reach 1.85");
    }

    [Fact]
    public void SameMatchPair_NegativeEv_Rejected()
    {
        // 0.40 × 2.10 = 0.84 → EV −16%
        var pair = new SameMatchPair(1, "Premier League", 0.40, 1.40, 1.50);

        TicketBuilder.Build([], [], Strat, Opt, [pair]).Should().BeEmpty();
    }

    [Fact]
    public void GoalsMarketTickets_GetGuaranteedSlots()
    {
        // 4 goals-market legs + 4 winner legs, all valid combos.
        var legs = new List<TicketLeg>();
        for (var i = 1; i <= 4; i++)
            legs.Add(new TicketLeg(i, $"League{i}", "over25", "Over 2.5 Goals", 0.62, 1.85, 0.147));
        for (var i = 5; i <= 8; i++)
            legs.Add(new TicketLeg(i, $"League{i}", "match_winner", "Match Winner (Home)", 0.70, 2.20, 0.54));

        var tickets = TicketBuilder.Build([], legs, Strat, Opt);
        var combos = tickets.Where(t => !t.IsSingle && !t.IsSameMatchPair).ToList();

        combos.Count(t => t.ContainsGoalsMarket)
            .Should().BeGreaterThanOrEqualTo(3,
                "BTTS/Over2.5 tickets are the focus markets and get guaranteed slots");
    }

    [Fact]
    public void MinOddsSameMatchPair_IsConfigurable()
    {
        var strat = new StrategyOptions { MinOddsSameMatchPair = 2.50 };
        var pair = new SameMatchPair(1, "Premier League", 0.55, 1.55, 1.35); // 2.09

        TicketBuilder.Build([], [], strat, Opt, [pair]).Should().BeEmpty();
    }
}

public class IsotonicShrinkageTests
{
    [Fact]
    public void ThinBlocks_StayCloseToTheRawProbability()
    {
        // 10 samples at p≈0.70 that all won: raw PAV would jump to 0.99.
        var samples = Enumerable.Range(0, 10).Select(i => (0.70 + i * 0.0001, true)).ToList();

        var model = IsotonicRegression.Fit(samples.Select(s => (s.Item1, s.Item2)).ToList());

        model.Predict(0.70).Should().BeLessThan(0.80,
            "10 observations must not move the estimate to near-certainty");
        model.Predict(0.70).Should().BeGreaterThan(0.70);
    }

    [Fact]
    public void DenseBlocks_ApplyTheFullCorrection()
    {
        // 1000 samples at p≈0.80 hitting only 50%
        var samples = Enumerable.Range(0, 1000)
            .Select(i => (0.80 + (i % 5) * 0.0001, i % 2 == 0)).ToList();

        var model = IsotonicRegression.Fit(samples.Select(s => (s.Item1, s.Item2)).ToList());

        model.Predict(0.80).Should().BeApproximately(0.52, 0.05,
            "with 1000 observations the correction is trusted almost fully");
    }

    [Fact]
    public void ShrinkageStrength_IsConfigurable()
    {
        var samples = Enumerable.Range(0, 50).Select(i => (0.60 + i * 0.0001, true)).ToList()
            .Select(s => (s.Item1, s.Item2)).ToList();

        var trusting = IsotonicRegression.Fit(samples, priorStrength: 1);
        var cautious = IsotonicRegression.Fit(samples, priorStrength: 500);

        trusting.Predict(0.60).Should().BeGreaterThan(cautious.Predict(0.60));
    }
}
