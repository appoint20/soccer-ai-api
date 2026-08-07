using FluentAssertions;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class TicketBuilderTests
{
    private static readonly StrategyOptions Strat = new();
    private static readonly ConfluenceOptions Opt = new();

    private static TicketLeg Leg(int fixtureId, string league = "Premier League",
        string market = "over25", double p = 0.60, double odds = 1.90, double ev = 0.14) =>
        new(fixtureId, league, market, market, p, odds, ev);

    [Fact]
    public void Single_MeetsMarketFloor_BecomesTicketOfOne()
    {
        var tickets = TicketBuilder.Build([Leg(1, odds: 1.80)], [], Strat, Opt);

        tickets.Should().ContainSingle();
        tickets[0].IsSingle.Should().BeTrue();
        tickets[0].TotalOdds.Should().Be(1.80);
        tickets[0].KellyStake.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Single_BelowFloor_NotATicket_ButUsableAsComboLeg()
    {
        // 1.55 < 1.70 goals floor: no single ticket...
        var subFloor = Leg(1, odds: 1.55, p: 0.68, ev: 0.054);
        var partner = Leg(2, league: "Bundesliga", odds: 1.60, p: 0.66, ev: 0.056);

        // Combo legs must be qualified (v8): pass them as qualified singles.
        var tickets = TicketBuilder.Build([subFloor, partner], [], Strat, Opt);

        tickets.Should().ContainSingle("no single, but the 2-leg combo clears the ticket floor");
        var combo = tickets[0];
        combo.Legs.Should().HaveCount(2);
        combo.TotalOdds.Should().BeApproximately(1.55 * 1.60, 0.01);
        combo.TotalOdds.Should().BeGreaterThanOrEqualTo(1.70);
    }

    [Fact]
    public void Combo_With1X2Leg_NeedsHigherTicketFloor()
    {
        // Product 1.30 × 1.55 = 2.015: ≥ 1.70 but < 2.10 → fails because a 1X2 leg is involved
        var winner = Leg(1, market: "match_winner", odds: 1.30, p: 0.80, ev: 0.04);
        var goals = Leg(2, league: "Bundesliga", odds: 1.55, p: 0.68, ev: 0.054);

        var tickets = TicketBuilder.Build([winner, goals], [], Strat, Opt);

        tickets.Should().BeEmpty("2.015 < the 2.10 floor that applies when 1X2 legs are involved");
    }

    [Fact]
    public void Combo_NeverTwoLegsFromTheSameFixture()
    {
        // One fixture, one leg — always. Two markets on the same match are
        // correlated at best and mutually exclusive at worst (Over 2.5 with
        // Under 2.5 can never both land). Legs from the same LEAGUE are allowed;
        // see MaxLegsPerLeague.
        var legs = new List<TicketLeg>
        {
            Leg(1, "Premier League", "over25", 0.66, 1.60, 0.056),
            Leg(1, "Premier League", "btts", 0.65, 1.70, 0.105),      // same fixture
            Leg(2, "Premier League", "over25", 0.66, 1.60, 0.056),
            Leg(3, "Bundesliga", "over25", 0.66, 1.60, 0.056)
        };

        var tickets = TicketBuilder.Build(legs, [], Strat, Opt);

        tickets.Should().NotBeEmpty();
        foreach (var ticket in tickets)
            ticket.Legs.Select(l => l.FixtureId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Combo_OnlyEvPositiveTickets()
    {
        var tickets = TicketBuilder.Build(
            [Leg(1, p: 0.55, odds: 1.30, ev: 0.01), Leg(2, league: "Bundesliga", p: 0.55, odds: 1.35, ev: 0.01)],
            [], Strat, Opt);

        // combined p = 0.3025, odds = 1.755 → EV = −0.47 → rejected
        tickets.Should().BeEmpty("ticket EV must be positive");
    }

    [Fact]
    public void Combo_PrefersHighProbabilityFavorites()
    {
        var legs = new List<TicketLeg>();
        // 8 mid-probability legs + 2 favorite legs (p >= 0.65)
        for (var i = 1; i <= 8; i++)
            legs.Add(Leg(i, $"League{i}", p: 0.58, odds: 1.95, ev: 0.13));
        legs.Add(Leg(9, "League9", market: "match_winner", p: 0.70, odds: 1.55, ev: 0.085));
        legs.Add(Leg(10, "League10", market: "match_winner", p: 0.68, odds: 1.60, ev: 0.088));

        var tickets = TicketBuilder.Build(legs, [], Strat, Opt);

        tickets.Should().NotBeEmpty();
        // The favorites must appear in the built tickets (preference ordering)
        tickets.SelectMany(t => t.Legs).Should().Contain(l => l.Probability >= 0.65);
    }

    [Fact]
    public void KellyStake_AtTicketLevel_FromCombinedProbability()
    {
        var a = Leg(1, "Premier League", p: 0.70, odds: 1.80, ev: 0.26);
        var b = Leg(2, "Bundesliga", p: 0.70, odds: 1.80, ev: 0.26);

        var tickets = TicketBuilder.Build([a, b], [], Strat, Opt);

        var combo = tickets.Single(t => t.Legs.Count == 2);
        combo.CombinedProbability.Should().BeApproximately(0.49, 1e-9);
        combo.TotalOdds.Should().BeApproximately(3.24, 0.01);
        // full Kelly = (0.49×3.24 − 1)/(3.24 − 1) = 0.5876/2.24 ≈ 0.2623 → quarter ≈ 0.0656
        combo.KellyStake.Should().BeApproximately(0.0656, 0.001);
    }

    [Fact]
    public void ByDefault_NoTicketHasMoreThanTwoLegs()
    {
        var legs = Enumerable.Range(1, 6)
            .Select(i => Leg(i, $"League{i}", p: 0.62, odds: 1.85, ev: 0.147))
            .ToList();

        var tickets = TicketBuilder.Build(legs, [], Strat, Opt);

        tickets.Should().NotBeEmpty();
        tickets.Should().OnlyContain(t => t.Legs.Count <= 2,
            "3-leg tickets measured 16% hit / −22.7% Kelly in baseline v5");
    }

    [Fact]
    public void ThreeLegs_StillPossible_WhenConfigured()
    {
        var opt = new ConfluenceOptions { MaxComboLegs = 3 };
        var legs = Enumerable.Range(1, 6)
            .Select(i => Leg(i, $"League{i}", p: 0.62, odds: 1.85, ev: 0.147))
            .ToList();

        var tickets = TicketBuilder.Build(legs, [], Strat, opt);

        tickets.Should().Contain(t => t.Legs.Count == 3);
    }

    [Fact]
    public void MarketFloor_Mapping()
    {
        TicketBuilder.MarketFloor("match_winner", Strat).Should().Be(2.10);
        TicketBuilder.MarketFloor("draw", Strat).Should().Be(2.10);
        TicketBuilder.MarketFloor("over25", Strat).Should().Be(1.70);
        TicketBuilder.MarketFloor("btts", Strat).Should().Be(1.70);
    }
}
