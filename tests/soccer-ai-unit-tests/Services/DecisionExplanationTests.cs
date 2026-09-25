using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class DecisionExplanationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static MatchAnalysis Match(DateTimeOffset? kickoff = null) => new()
    {
        Id = 1, Date = kickoff ?? Now.AddHours(8), HomeTeam = "Home", AwayTeam = "Away", OddsBookmaker = "Bet365",
        Prediction = new PredictionResponse(),
        DecisionAudit = new(2, [new("btts", .7, .5, true, 3, 0, true, [])
        {
            Odds = 1.9, MinOdds = 1.7, MinEdge = .05, Ev = .33, ComboEligible = true,
            GateOutcome = GateOutcome.Qualified, AiAgrees = true, Selection = "BTTS"
        }], Now)
    };

    private static AiDecisionExplanation Explanation(MatchAnalysis m)
    {
        var input = DecisionExplanationPolicy.Input(m);
        AiDecisionLanguage Block(bool de) => new()
        {
            SummaryLines = de ? AiSummarySamples.German : AiSummarySamples.English,
            Markets = input.Markets.Select(x => new AiMarketExplanation { Market = x.Market, Checks = x.Facts.ToList() }).ToList()
        };
        return new() { FixtureId = m.Id, InputHash = DecisionExplanationPolicy.Hash(input), En = Block(false), De = Block(true) };
    }

    [Fact]
    public void SixSentencesEndWithTheActualDecisionAndUseOnlyVisibleMarkets()
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.AiGenerated.Should().BeTrue();
        m.Presentation.SummaryLines.Should().HaveCount(6);
        m.Presentation.SummaryLines[4].Should().Contain("selects: Both teams to score");
        m.Presentation.SummaryLines[5].Should().Contain("70%").And.Contain("evidence checks")
            .And.NotContain("1.90", "the closing line states the case, not the price");
        // Two, because this fixture's audit carries no fired evidence rules and
        // no provider read: the AI's view and the verdict. The count follows the
        // evidence rather than being padded to a fixed five.
        m.Presentation.Markets.Single().Checks.Should().HaveCount(2);
    }

    /// <summary>
    /// The summary explains match evidence and never quotes prices. Updating
    /// informational odds must preserve it instead of showing two closing lines.
    /// </summary>
    [Theory]
    [InlineData(1.69)]
    [InlineData(1.70)]
    public void APriceMovePreservesTheFullSummaryAndSelection(double odds)
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        var f = new Fixture { Id = 1, Date = m.Date, OddsBookmaker = "Bet365", BttsYesOdds = odds,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now.AddMinutes(-5) };

        LiveOddsPolicy.RefreshResponse(m, f, Now);

        m.Presentation!.AiGenerated.Should().BeTrue("price is not evidence in the match summary");
        m.Presentation.SummaryLines.Should().HaveCount(6);
        m.Presentation.SummaryLines.Take(4).Should().Equal(AiSummarySamples.English);
        string.Join(" ", m.Presentation.SummaryLines).Should().Contain("selects: Both teams to score");
        m.DecisionAudit!.Markets.Single().Qualified.Should().BeTrue();
    }

    [Fact]
    public void ProviderTimestampRefreshWithoutPriceChangeDoesNotSpendAnotherAiCall()
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        m.OddsUpdatedAtUtc = Now; m.OddsCheckedAtUtc = Now;
        DecisionExplanationPolicy.IsCurrent(m, m.DecisionExplanation).Should().BeTrue();
    }

    [Fact]
    public void EvidenceChangesAndOldSummaryContractsStillRequireRegeneration()
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        var oldInput = DecisionExplanationPolicy.Input(m) with { Version = 1 };
        m.DecisionExplanation.InputHash = DecisionExplanationPolicy.Hash(oldInput);
        DecisionExplanationPolicy.IsCurrent(m, m.DecisionExplanation).Should().BeFalse();

        m.DecisionExplanation = Explanation(m);
        m.DecisionAudit = m.DecisionAudit! with
        {
            Markets = [m.DecisionAudit.Markets[0] with { Probability = .65 }]
        };
        DecisionExplanationPolicy.IsCurrent(m, m.DecisionExplanation).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingHistoricalSummariesStayReadableButUpcomingOnesRequireTheNewContract(bool upcoming)
    {
        var m = Match(DateTimeOffset.UtcNow.AddDays(upcoming ? 1 : -1));
        m.DecisionExplanation = Explanation(m);
        m.DecisionExplanation.En.SummaryLines = ["Attack is balanced.", "Defence is vulnerable.", "The signals differ.", "The sample is limited."];
        var input = DecisionExplanationPolicy.Input(m);
        var oldInput = input with { Version = 1, Markets = input.Markets.Select(x => x with
        {
            Odds = m.DecisionAudit!.Markets.Single(a => a.Market == x.Market).Odds, Bookmaker = m.OddsBookmaker
        }).ToList() };
        m.DecisionExplanation.InputHash = DecisionExplanationPolicy.Hash(oldInput);
        DecisionExplanationPolicy.IsCurrent(m, m.DecisionExplanation).Should().BeFalse();
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.AiGenerated.Should().Be(!upcoming);
        m.Presentation.SummaryLines.Should().HaveCount(upcoming ? 2 : 6);

        m.DecisionAudit = m.DecisionAudit! with { Markets = [m.DecisionAudit.Markets[0] with { Probability = .65 }] };
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.AiGenerated.Should().BeFalse("legacy wording must still match the original evidence");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ContextLengthLeavesRoomForExactlyTwoAuthoritativeClosingSentences(int count)
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        foreach (var block in new[] { m.DecisionExplanation.En, m.DecisionExplanation.De })
            while (block.SummaryLines.Count < count)
                block.SummaryLines.Add("The weaker attacking evidence leaves room for a quieter match than the defensive records suggest.");
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.AiGenerated.Should().BeTrue();
        m.Presentation.SummaryLines.Should().HaveCount(count + 2);
        m.Presentation.SummaryLines[^2].Should().Contain("selects: Both teams to score");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void OutOfRangeContextIsRejected(int count)
    {
        var m = Match(); var result = Explanation(m);
        result.De.SummaryLines = Enumerable.Repeat(AiSummarySamples.German[0], count).ToList();
        DecisionExplanationPolicy.Invalid(result, DecisionExplanationPolicy.Input(m)).Should().NotBeNull();
    }

    [Fact]
    public void FourTerseLabelsAreNotAValidMatchSummary()
    {
        var m = Match(); var result = Explanation(m);
        result.En.SummaryLines = ["Attack is balanced.", "Defence is vulnerable.", "The signals differ.", "The sample is limited."];
        DecisionExplanationPolicy.Invalid(result, DecisionExplanationPolicy.Input(m)).Should().Contain("at least 50 words");
    }

    /// <summary>
    /// A thin edge is reported, not enforced. EV follows the new price so the
    /// reader can see what the market pays for the same call.
    /// </summary>
    [Fact]
    public void AThinEdgeIsRepricedRatherThanWithdrawn()
    {
        var m = Match();
        m.DecisionAudit = m.DecisionAudit! with { Markets = [m.DecisionAudit.Markets[0] with { Probability = .6 }] };
        var f = new Fixture { Date = m.Date, OddsBookmaker = "Bet365", BttsYesOdds = 1.71,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now };

        LiveOddsPolicy.RefreshResponse(m, f, Now);

        m.DecisionAudit.Markets[0].Qualified.Should().BeTrue();
        m.DecisionAudit.Markets[0].ComboEligible.Should().BeTrue();
        m.DecisionAudit.Markets[0].GateOutcome.Should().Be(GateOutcome.Qualified);
        m.DecisionAudit.Markets[0].Ev.Should().BeApproximately(.6 * 1.71 - 1, 1e-9);
    }

    [Fact]
    public void GermanFallbackIsReadableWithNoRawEnglishOrPlaceholder()
    {
        var m = Match(); DecisionExplanationPolicy.Refresh(m, "de");
        var checks = m.Presentation!.Markets.Single().Checks;
        // Counts, the AI's view and the verdict — the measured evidence is
        // English until the writer has rewritten it.
        checks.Should().HaveCount(4);
        string.Join(" ", checks).Should().NotContain("n/a").And.NotContain("Scored");
        m.Presentation.SummaryLines[0].Should().Contain("Das System wählt");
    }

    [Fact]
    public void HidingAMarketCardCannotTurnARecordedSelectionIntoANoBetClaim()
    {
        var m = Match();
        m.DecisionAudit = new(2, [m.DecisionAudit!.Markets[0] with {
            Market = "draw", Selection = "Draw", Probability = .35, Threshold = .30, Odds = 3.5 }], Now);
        DecisionExplanationPolicy.Input(m).SelectedMarkets.Should().ContainSingle("draw");
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.Markets.Should().BeEmpty();
        m.Presentation.SummaryLines[0].Should().Contain("selects: Draw");
    }

    [Fact]
    public void WrongMarketUnsupportedNumbersLongOrEmptyTextAreRejected()
    {
        var m = Match(); var input = DecisionExplanationPolicy.Input(m);
        var result = Explanation(m); result.De.Markets[0].Market = "under25";
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
        result = Explanation(m); result.En.Markets[0].Checks[0] = "This has a 99% chance.";
        DecisionExplanationPolicy.Invalid(result, input).Should().Contain("unsupported number");
        result = Explanation(m); result.De.SummaryLines[0] = new string('x', 261);
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
        result = Explanation(m); result.De.Markets[0].Checks[1] = "n/a";
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
        result = Explanation(m); result.En.SummaryLines[0] = "This is our selected bet.";
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
    }
}
