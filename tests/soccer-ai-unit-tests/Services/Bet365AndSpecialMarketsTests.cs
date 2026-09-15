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
    public void TwoToThreeGoalsCanQualifyOnlyWithAnActualPriceAndEvidence()
    {
        var opt = new ConfluenceOptions();
        var priced = ConfluenceRuleEngine.EvaluateGoals23(.6, Evidence(), .5, 1.9, 1.7, .05, opt);
        priced.Qualified.Should().BeTrue();
        var missing = ConfluenceRuleEngine.EvaluateGoals23(.6, Evidence(), .5, null, 1.7, .05, opt);
        missing.Qualified.Should().BeFalse(); missing.Odds.Should().BeNull(); missing.Ev.Should().BeNull();
    }

    [Theory]
    [InlineData(1.8, true)]
    [InlineData(1.69, false)]
    [InlineData(null, false)]
    public void CombinedMarketUsesItsOwnQuoteAndJointWhileIndividualOddsAreBelow170(double? quote, bool expected)
    {
        var p = new WeightedPrediction { BTTSProb = .8, Over25Prob = .8, HomeProb = .5, AwayProb = .3, DrawProb = .2 };
        var opt = new ConfluenceOptions();
        var audit = ConfluenceRuleEngine.Evaluate(p, Evidence(),
            MarketPrices.FromRaw(null, null, null, 1.4, null, 1.5, null, quote), 0, opt, new StrategyOptions(),
            new AiAnalysisDto { AiOverallConfidence = 70, AiBttsAndOver25Qualified = true }, .7);
        var combined = audit.Markets.Single(m => m.Market == "btts_and_over25");
        combined.Qualified.Should().Be(expected);
        combined.Probability.Should().Be(.7); combined.Odds.Should().Be(quote);
        audit.Markets.Single(m => m.Market == "btts").Qualified.Should().BeFalse();
        audit.Markets.Single(m => m.Market == "over25").Qualified.Should().BeFalse();
        var selection = PickSelector.Select(new(1, "League", "A", "B", Now.AddHours(5)), audit, .7, opt);
        var tickets = PickSelector.BuildTickets([selection], new StrategyOptions(), opt);
        if (expected) tickets.Should().ContainSingle(t => t.TotalOdds == quote && t.Legs.Single().Market == "btts_and_over25");
        else tickets.Where(t => t.IsPriced).Should().BeEmpty();
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
    private sealed class Reply(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
