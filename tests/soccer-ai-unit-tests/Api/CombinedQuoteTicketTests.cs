using FluentAssertions;
using Mediator.Net.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Picks;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Api;

public class CombinedQuoteTicketTests
{
    [Theory]
    [InlineData(1.80, true)]
    [InlineData(null, false)]
    [InlineData(1.69, false)]
    public async Task CustomBttsOverRequestResolvesToOneGenuineCombinedSelection(double? combinedQuote, bool allowed)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        var f = new Fixture { Id = 1, Date = now.AddHours(6), OddsBookmaker = "Bet365", OddsCheckedAtUtc = now,
            OddsUpdatedAtUtc = now, BttsYesOdds = 1.5, Over25Odds = 1.4, BttsAndOver25Odds = combinedQuote };
        MarketRuleAudit Market(string key, double p, double odds) => new(key, p, .6, true, 3, 0, true, [])
        { Odds = odds, MinOdds = 1.7, MinEdge = .05, GateOutcome = GateOutcome.Qualified };
        var snapshot = new MatchAnalysis { Id = 1, Date = f.Date, BttsAndOver25Probability = .7,
            DecisionAudit = new(2, [Market("btts", .8, 2), Market("over25", .8, 2), Market("btts_and_over25", .7, 2)], now) };
        db.Fixtures.Add(f);
        db.FixtureAnalyses.Add(new FixtureAnalysis { FixtureId = 1, Lang = "en", SnapshotJson = AnalysisSnapshotSerializer.Serialize(snapshot) });
        await db.SaveChangesAsync();
        var query = new PriceCustomTicketQuery { Legs = [new() { FixtureId = 1, Market = "btts" }, new() { FixtureId = 1, Market = "over25" }] };
        var context = new Mock<IReceiveContext<PriceCustomTicketQuery>>(); context.SetupGet(c => c.Message).Returns(query);
        var response = await new PriceCustomTicketHandler(db, Options.Create(new ConfluenceOptions())).Handle(context.Object, CancellationToken.None);
        if (allowed)
        {
            response.Ticket.Should().NotBeNull();
            response.Ticket!.Legs.Should().ContainSingle().Which.Market.Should().Be("btts_and_over25");
            response.Ticket.TotalOdds.Should().Be(1.80);
            response.Ticket.Probability.Should().Be(.7);
        }
        else response.Ticket.Should().BeNull("neither missing quotes nor a price below 1.70 can be replaced by a product of leg odds");
    }
}
