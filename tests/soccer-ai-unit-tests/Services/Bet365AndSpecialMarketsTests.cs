using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class Bet365AndSpecialMarketsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static StrategicSignals Evidence() => new()
    {
        HomeScoring = new() { ScoredInLast3Venue = SignalValue.Of(3, true, "Scored in 3/3"),
            ConcededInLast3Venue = SignalValue.Of(3, true, "Conceded in 3/3"), AvgTotalGoalsLast5 = SignalValue.Of(2.5, true, "2.5 total goals") },
        AwayScoring = new() { ScoredInLast3Venue = SignalValue.Of(3, true, "Scored in 3/3"),
            ConcededInLast3Venue = SignalValue.Of(3, true, "Conceded in 3/3"), AvgTotalGoalsLast5 = SignalValue.Of(2.5, true, "2.5 total goals") },
        H2H = new() { SampleSize = 5, AvgTotalGoals = SignalValue.Of(2.5, true, "2.5 goals"),
            Over25RateLast5 = SignalValue.Of(.8, true, "80% over"), BttsRateLast5 = SignalValue.Of(.8, true, "80% both score") }
    };

    [Fact]
    public void LivePriceUsesLatestBet365QuoteEvenWhenOtherBookmakersOrOlderQuotesAreHigher()
    {
        var f = new Fixture { Date = Now.AddHours(5) };
        FixtureOddsWriter.ReplaceLivePrices(f, [
            new("Bet365", OddsMarkets.BttsYes, 2.1, Now.AddHours(-2)),
            new("Bet365", OddsMarkets.BttsYes, 1.65, Now.AddMinutes(-10)),
            new("Pinnacle", OddsMarkets.BttsYes, 2.3, Now.AddMinutes(-1))], Now);
        f.BttsYesOdds.Should().Be(1.65); f.OddsBookmaker.Should().Be("Bet365");
        f.OddsUpdatedAtUtc.Should().Be(Now.AddMinutes(-10));
        LiveOddsPolicy.IsFresh(f, Now).Should().BeTrue();
        FixtureOddsWriter.ReplaceLivePrices(f, [new("Pinnacle", OddsMarkets.BttsYes, 2.4, Now)], Now);
        f.BttsYesOdds.Should().BeNull("there must be no silent fallback to a different bookmaker");
        LiveOddsPolicy.IsFresh(f, Now).Should().BeFalse();
    }

    [Fact]
    public void LegacyMixedBookmakerPricesAreNotPresentedAsLiveBet365()
    {
        var f = new Fixture { Date = Now.AddHours(5), OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now, BttsYesOdds = 2 };
        LiveOddsPolicy.IsFresh(f, Now).Should().BeFalse();
    }

    [Fact]
    public void TwoToThreeGoalsQualifiesOnEvidenceWithOrWithoutAPrice()
    {
        var opt = new ConfluenceOptions();
        var priced = ConfluenceRuleEngine.EvaluateGoals23(.6, Evidence(), .5, 1.9, 1.7, .05, opt);
        priced.Qualified.Should().BeTrue();
        var missing = ConfluenceRuleEngine.EvaluateGoals23(.6, Evidence(), .5, null, 1.7, .05, opt);
        missing.Qualified.Should().BeTrue("the same evidence supports the same call");
        missing.Odds.Should().BeNull(); missing.Ev.Should().BeNull();
    }

    /// <summary>
    /// The combined market is priced by its own quote — never the product of
    /// the two single prices — and carries the joint probability off the score
    /// matrix. Its quote no longer decides whether the market is called.
    /// </summary>
    [Theory]
    [InlineData(1.8)]
    [InlineData(1.69)]
    [InlineData(null)]
    public void TheCombinedMarketCarriesItsOwnQuoteAndTheJointProbability(double? quote)
    {
        var p = new WeightedPrediction { BTTSProb = .8, Over25Prob = .8, HomeProb = .5, AwayProb = .3, DrawProb = .2 };
        var opt = new ConfluenceOptions();
        var audit = ConfluenceRuleEngine.Evaluate(p, Evidence(),
            MarketPrices.FromRaw(null, null, null, 1.4, null, 1.5, null, quote), 0, opt, new StrategyOptions(),
            new AiAnalysisDto { AiOverallConfidence = 70, AiBttsAndOver25Qualified = true }, .7);

        var combined = audit.Markets.Single(m => m.Market == "btts_and_over25");
        combined.Probability.Should().Be(.7, "the joint probability, not p_btts × p_over25");
        combined.Odds.Should().Be(quote);
        combined.Qualified.Should().BeTrue("evidence and probability decide this, not the quote");
    }

    /// <summary>
    /// A ticket still needs a real price: there is nothing to multiply without
    /// one. An unpriced call is published as analysis, not as a priced ticket.
    /// </summary>
    [Theory]
    [InlineData(1.8)]
    [InlineData(null)]
    public void OnlyALegWithAQuoteBuildsAPricedTicket(double? quote)
    {
        var p = new WeightedPrediction { BTTSProb = .8, Over25Prob = .8, HomeProb = .5, AwayProb = .3, DrawProb = .2 };
        var opt = new ConfluenceOptions();
        var audit = ConfluenceRuleEngine.Evaluate(p, Evidence(),
            MarketPrices.FromRaw(null, null, null, null, null, null, null, quote), 0, opt, new StrategyOptions(),
            new AiAnalysisDto { AiOverallConfidence = 70, AiBttsAndOver25Qualified = true }, .7);

        var selection = PickSelector.Select(new(1, "League", "A", "B", Now.AddHours(5)), audit, .7, opt);
        var tickets = PickSelector.BuildTickets([selection], new StrategyOptions(), opt);

        if (quote is null) tickets.Where(t => t.IsPriced).Should().BeEmpty();
        else tickets.Should().ContainSingle(t => t.TotalOdds == quote && t.Legs.Single().Market == "btts_and_over25");
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(3, 0, false)]
    [InlineData(2, 1, true)]
    public void CombinedOutcomeRequiresBothConditions(int home, int away, bool expected) =>
        MarketOutcome.Won("btts_and_over25", "", home, away).Should().Be(expected);

    [Fact]
    public async Task ParserRecognizesOnlyExplicitFullTimeRangeAndCombinedQuotes()
    {
        // Contract fixture, not a claim of current bet365 coverage. Half-time,
        // team totals and separate exact scores must never masquerade as these markets.
        var payload = JsonSerializer.Serialize(new { errors = new { }, paging = new { total = 1 }, response = new[] {
            new { update = Now.ToString("O"), bookmakers = new[] { new { name = "Bet365", bets = new[] {
                new { name = "Goals Range", values = new[] { new { value = "2-3", odd = "1.90" } } },
                new { name = "Total Goals/Both Teams Score", values = new[] { new { value = "Over 2.5/Yes", odd = "1.85" } } },
                new { name = "Goals Range - First Half", values = new[] { new { value = "2-3", odd = "4.20" } } },
                new { name = "Exact Goals Number", values = new[] { new { value = "2", odd = "3.10" }, new { value = "3", odd = "4.10" } } }
            } } } }
        } });
        using var client = new HttpClient(new Reply(payload)) { BaseAddress = new Uri("https://football.test") };
        var api = new ApiFootballService(client, Mock.Of<IApiQuotaTracker>(), Mock.Of<IApiCallTracker>(), NullLogger<ApiFootballService>.Instance);
        var quotes = await api.GetFixtureOddsQuotesAsync(1);
        quotes.Should().HaveCount(2);
        quotes.Should().Contain(q => q.Market == OddsMarkets.Goals23 && q.Price == 1.9);
        quotes.Should().Contain(q => q.Market == OddsMarkets.BttsAndOver25 && q.Price == 1.85);
    }
    /// <summary>
    /// The provider sends goal-count selections as integers (captured from a live
    /// response). Reading them as strings threw and discarded the whole response,
    /// so a fixture offering "Exact Goals Number" lost its 1X2, over/under and BTTS
    /// prices too. The sample above uses strings, which is why it never caught this.
    /// </summary>
    [Fact]
    public async Task NumericSelectionsDoNotDiscardTheRestOfTheResponse()
    {
        var payload = """
            {"errors": {}, "paging": {"total": 1}, "response": [{
              "update": "UPDATED_AT",
              "bookmakers": [{"name": "Bet365", "bets": [
                {"name": "Exact Goals Number", "values": [{"value": 0, "odd": "7.50"}, {"value": 2, "odd": "3.40"}]},
                {"name": "Match Winner", "values": [{"value": "Home", "odd": "2.10"}, {"value": "Draw", "odd": "3.40"}, {"value": "Away", "odd": "3.60"}]},
                {"name": "Goals Over/Under", "values": [{"value": "Over 2.5", "odd": 1.95}, {"value": "Under 2.5", "odd": "1.85"}]}
              ]}]
            }]}
            """.Replace("UPDATED_AT", Now.ToString("O"));
        using var client = new HttpClient(new Reply(payload)) { BaseAddress = new Uri("https://football.test") };
        var api = new ApiFootballService(client, Mock.Of<IApiQuotaTracker>(), Mock.Of<IApiCallTracker>(), NullLogger<ApiFootballService>.Instance);

        var quotes = await api.GetFixtureOddsQuotesAsync(1);

        quotes.Should().Contain(q => q.Market == OddsMarkets.HomeWin && q.Price == 2.1);
        quotes.Should().Contain(q => q.Market == OddsMarkets.Draw && q.Price == 3.4);
        quotes.Should().Contain(q => q.Market == OddsMarkets.AwayWin && q.Price == 3.6);
        quotes.Should().Contain(q => q.Market == OddsMarkets.Over25 && q.Price == 1.95, "an odd sent as a number is still a price");
        quotes.Should().Contain(q => q.Market == OddsMarkets.Under25 && q.Price == 1.85);
        quotes.Should().NotContain(q => q.Market == OddsMarkets.Goals23, "single goal counts are not a 2-3 range");
    }

    private sealed class Reply(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
