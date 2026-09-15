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
    private static MatchAnalysis Match() => new()
    {
        Id = 1, Date = Now.AddHours(8), HomeTeam = "Home", AwayTeam = "Away", OddsBookmaker = "Bet365",
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
        AiDecisionLanguage Block() => new()
        {
            SummaryLines = ["The attacks are closely matched.", "Both defences have weaknesses.",
                "The recent evidence is mixed.", "The limited sample leaves uncertainty."],
            Markets = input.Markets.Select(x => new AiMarketExplanation { Market = x.Market, Checks = x.Facts.ToList() }).ToList()
        };
        return new() { FixtureId = m.Id, InputHash = DecisionExplanationPolicy.Hash(input), En = Block(), De = Block() };
    }

    [Fact]
    public void SixSentencesEndWithTheActualDecisionAndUseOnlyVisibleMarkets()
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        DecisionExplanationPolicy.Refresh(m);
        m.Presentation!.AiGenerated.Should().BeTrue();
        m.Presentation.SummaryLines.Should().HaveCount(6);
        m.Presentation.SummaryLines[4].Should().Contain("selects: Both teams to score");
        m.Presentation.SummaryLines[5].Should().Contain("1.90").And.Contain("70%");
        m.Presentation.Markets.Single().Checks.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(1.69, "below 1.70")]
    [InlineData(1.70, "selects:")]
    public void PriceDropCannotLeaveAnOldAiBetRecommendation(double odds, string expected)
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        var f = new Fixture { Id = 1, Date = m.Date, OddsBookmaker = "Bet365", BttsYesOdds = odds,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now.AddMinutes(-5) };
        LiveOddsPolicy.RefreshResponse(m, f, Now);
        m.Presentation!.AiGenerated.Should().BeFalse("the AI wording referred to a different price");
        string.Join(" ", m.Presentation.SummaryLines).Should().Contain(expected);
        if (odds < 1.7) string.Join(" ", m.Presentation.SummaryLines).Should().Contain("selects no bet");
        m.DecisionAudit!.Markets.Single().Qualified.Should().Be(odds >= 1.7);
    }

    [Fact]
    public void ProviderTimestampRefreshWithoutPriceChangeDoesNotSpendAnotherAiCall()
    {
        var m = Match(); m.DecisionExplanation = Explanation(m);
        m.OddsUpdatedAtUtc = Now; m.OddsCheckedAtUtc = Now;
        DecisionExplanationPolicy.IsCurrent(m, m.DecisionExplanation).Should().BeTrue();
    }

    [Fact]
    public void APriceDropBelowTheRequiredEdgeWithdrawsTheComboEvenAbove170()
    {
        var m = Match();
        m.DecisionAudit = m.DecisionAudit! with { Markets = [m.DecisionAudit.Markets[0] with { Probability = .6 }] };
        var f = new Fixture { Date = m.Date, OddsBookmaker = "Bet365", BttsYesOdds = 1.71,
            OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now };
        LiveOddsPolicy.RefreshResponse(m, f, Now);
        m.DecisionAudit.Markets[0].Qualified.Should().BeFalse();
        m.DecisionAudit.Markets[0].ComboEligible.Should().BeFalse();
        m.DecisionAudit.Markets[0].GateOutcome.Should().Be(GateOutcome.BelowMinEdge);
    }

    [Fact]
    public void GermanFallbackHasFiveReadableChecksAndNoRawEnglishOrPlaceholder()
    {
        var m = Match(); DecisionExplanationPolicy.Refresh(m, "de");
        var checks = m.Presentation!.Markets.Single().Checks;
        checks.Should().HaveCount(5);
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
        result = Explanation(m); result.De.SummaryLines[0] = new string('x', 181);
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
        result = Explanation(m); result.De.Markets[0].Checks[1] = "n/a";
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
        result = Explanation(m); result.En.SummaryLines[0] = "This is our selected bet.";
        DecisionExplanationPolicy.Invalid(result, input).Should().NotBeNull();
    }
}
