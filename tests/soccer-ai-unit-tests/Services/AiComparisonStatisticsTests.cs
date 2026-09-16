using System.Text.Json;
using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Statistics;

namespace soccer_ai_unit_tests.Services;

public class AiComparisonStatisticsTests
{
    private static readonly DateTimeOffset Kickoff = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
    private static AiAnalysisDto Ai(DateTimeOffset? generated = null) => new() {
        AiOverallConfidence = 70, GeneratedAtUtc = generated ?? Kickoff.AddHours(-4),
        ModelVersion = "provider/model-v1", PromptHash = "prompt-hash", InputHash = "fixture-input-hash" };
    private static MarketRuleAudit Market(bool model, bool combined, bool agrees, double odds = 2) =>
        new("btts", .65, .58, true, 2, 0, combined, []) {
            ModelOnlyQualified = model, AiAgrees = agrees, AiAgreementMode = "Confirm", Odds = odds, MinOdds = 1.7 };
    private static (Fixture, PredictionSnapshot) Row(int id, bool won, MarketRuleAudit market,
        AiAnalysisDto? ai = null, bool fresh = true, int schema = 2) => (new Fixture {
            Id = id, Status = "FT", Date = Kickoff, HomeGoal = 2, AwayGoal = won ? 1 : 0
        }, new PredictionSnapshot {
            FixtureId = id, CapturedAtUtc = Kickoff.AddHours(-2), KickoffUtc = Kickoff,
            ContextJson = JsonSerializer.Serialize(new { schema, live_odds = fresh, ai = ai ?? Ai(),
                audit = new DecisionAudit(2, [market], Kickoff.AddHours(-2)) }) });

    [Fact]
    public void BothArmsUseSamePopulationAndExposeAddedRemovedAndStrictPicks()
    {
        var result = AiComparisonStatistics.Build([
            Row(1, true, Market(true, true, true)),
            Row(2, false, Market(true, false, false)),
            Row(3, false, Market(false, true, true)),
            Row(4, true, Market(false, false, false))]);
        result.ComparableFixtures.Should().Be(4);
        var market = result.Markets.Single(m => m.Market == "btts");
        market.Opportunities.Should().Be(4);
        market.ModelOnly.Picks.Should().Be(2); market.ModelOnly.Correct.Should().Be(1);
        market.Combined.HitRate.Should().Be(.5); market.Combined.Coverage.Should().Be(.5);
        market.StrictAgreement.HitRate.Should().Be(1); market.StrictAgreement.Coverage.Should().Be(.25);
        market.AddedByAi.Picks.Should().Be(1); market.AddedByAi.Correct.Should().Be(0);
        market.RemovedByAi.Picks.Should().Be(1); market.RemovedByAi.Correct.Should().Be(0);
        market.StrictAgreement.Lower95.Should().BeLessThan(.21);
    }

    [Fact]
    public void MissingLateUnverifiedAndUnpricedOpinionsCannotInflateComparison()
    {
        var result = AiComparisonStatistics.Build([
            Row(1, true, Market(true, true, true), new AiAnalysisDto()),
            Row(2, true, Market(true, true, true), new AiAnalysisDto { AiOverallConfidence = 80 }),
            Row(3, true, Market(true, true, true), Ai(Kickoff.AddHours(-1))),
            Row(4, true, Market(true, true, true), fresh: false),
            Row(5, true, Market(true, true, true), schema: 1),
            Row(6, true, Market(true, true, true, 1.69)),
            Row(7, true, Market(true, true, true) with { ModelOnlyQualified = null })]);
        result.RecordedFixtures.Should().Be(6); result.MissingAiFixtures.Should().Be(1);
        result.UnverifiedAiFixtures.Should().Be(3); result.StaleOddsFixtures.Should().Be(1);
        result.ComparableFixtures.Should().Be(1);
        result.Markets.Single(m => m.Market == "btts").Combined.HitRate.Should().BeNull();
    }

    [Fact]
    public void AiRejectionIsAnAbstentionRatherThanPredictionOfTheOppositeOutcome()
    {
        var result = AiComparisonStatistics.Build([Row(1, false, Market(false, false, false))]);
        var market = result.Markets.Single(m => m.Market == "btts");
        market.ModelOnly.Picks.Should().Be(0); market.Combined.Correct.Should().Be(0);
        market.Combined.HitRate.Should().BeNull(); market.StrictAgreement.Picks.Should().Be(0);
    }
}
