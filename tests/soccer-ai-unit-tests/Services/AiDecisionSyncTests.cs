using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class AiDecisionSyncTests
{
    [Fact]
    public async Task ExplanationReceivesFinalGateCachesUnchangedInputsAndDoesNotRewriteAiOpinion()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        db.Fixtures.Add(new Fixture { Id = 1, Date = now.AddHours(6), OddsBookmaker = "Bet365",
            OddsCheckedAtUtc = now, OddsUpdatedAtUtc = now.AddMinutes(-5), BttsYesOdds = 1.60 });
        db.FixtureAnalyses.AddRange(new FixtureAnalysis { FixtureId = 1, Lang = "en", Analysis = "Old AI opinion", AiBttsQualified = true },
            new FixtureAnalysis { FixtureId = 1, Lang = "de", Analysis = "Alte KI-Einschätzung", AiBttsQualified = true });
        await db.SaveChangesAsync();
        var provider = new Mock<IAiAnalysisService>();
        provider.SetupGet(p => p.SupportsDecisionExplanations).Returns(true);
        provider.Setup(p => p.ExplainDecisionAsync(It.IsAny<DecisionExplanationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DecisionExplanationInput input, CancellationToken _) =>
            {
                input.Markets.Single().Qualified.Should().BeFalse("the price gate runs after the AI opinion");
                input.Markets.Single().Odds.Should().Be(1.60);
                AiDecisionLanguage Block() => new() { SummaryLines = ["Attack is balanced.", "Defence is vulnerable.", "The signals differ.", "The sample is limited."],
                    Markets = input.Markets.Select(m => new AiMarketExplanation { Market = m.Market, Checks = m.Facts.ToList() }).ToList() };
                return new AiDecisionExplanation { FixtureId = 1, ModelVersion = "test", GeneratedAtUtc = now, En = Block(), De = Block() };
            });
        var precompute = new Mock<IAnalysisPrecomputeService>();
        precompute.Setup(p => p.RecomputeFixtureAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            MatchAnalysis Snapshot() => new() { Id = 1, Date = now.AddHours(6), OddsBookmaker = "Bet365", Prediction = new(),
                DecisionExplanation = DecisionExplanationPolicy.Read(db.FixtureAnalyses.First(a => a.Lang == "en").DecisionExplanationJson),
                DecisionAudit = new(2, [new("btts", .7, .5, true, 2, 0, true, []) {
                    Odds = 2, MinOdds = 1.7, MinEdge = .05, Selection = "BTTS", GateOutcome = GateOutcome.Qualified, AiAgrees = true }], now) };
            return (IReadOnlyDictionary<string, MatchAnalysis>)new Dictionary<string, MatchAnalysis> { ["en"] = Snapshot(), ["de"] = Snapshot() };
        });
        var sync = new AiSyncService(db, Mock.Of<IMatchAnalysisService>(), provider.Object, precompute.Object,
            Mock.Of<ILeagueTierService>(), NullLogger<AiSyncService>.Instance);
        await sync.SyncSingleFixtureAsync(1);
        var en = await db.FixtureAnalyses.SingleAsync(a => a.Lang == "en");
        en.Analysis.Should().Be("Old AI opinion"); en.AiBttsQualified.Should().BeTrue();
        var snapshot = AnalysisSnapshotSerializer.Deserialize(en.SnapshotJson)!;
        snapshot.Presentation!.AiGenerated.Should().BeTrue();
        snapshot.Presentation.SummaryLines.Should().HaveCount(6);
        snapshot.Presentation.SummaryLines[4].Should().Contain("no bet");
        await sync.SyncSingleFixtureAsync(1);
        provider.Verify(p => p.ExplainDecisionAsync(It.IsAny<DecisionExplanationInput>(), It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
