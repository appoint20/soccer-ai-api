using System.Text.Json;
using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;

namespace soccer_ai_unit_tests.Services;

public sealed class MatchBriefingTests
{
    private static MatchAnalysis Match(IReadOnlyList<RuleResult>? rules = null) => new()
    {
        Id = 42, HomeTeam = "Home", AwayTeam = "Away", Date = DateTimeOffset.UtcNow.AddDays(1),
        Prediction = new()
        {
            HomeWin = new() { Probability = .45 }, Draw = new() { Probability = .3 }, AwayWin = new() { Probability = .25 },
            BTTS = new() { Probability = .32 }, Over25 = new() { Probability = .49 }, TwoToThreeGoals = new() { Probability = 0 }
        },
        Provider = new() { Home = new() { Played = 5, GoalsForAverage = .6, GoalsAgainstAverage = 1.2 } },
        DecisionAudit = new(1, [new("btts", .32, .5, false, 2, 1, false, rules ?? [])], DateTimeOffset.UtcNow),
        Ai = new() { Analysis = "Guaranteed winner! Bet everything on Home." }
    };

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void BriefingRetainsEveryGoalPatternWithoutTurningEstimatesIntoSelections(string language)
    {
        var result = MatchBriefingFactory.Create(Match(), language);
        result.GoalProfiles.Select(profile => profile.Probability).Should().Equal(.32, .49, 0);
        result.Outcomes.Select(outcome => outcome.Probability).Should().Equal(.45, .3, .25);
        result.AiGenerated.Should().BeFalse();
        result.SummaryLines.Should().NotContain(line => line.Contains("Guaranteed"));
        result.Sources.Should().OnlyContain(source => source.Status == "not_recorded");
        result.DataStatus.Should().Be("limited");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        foreach (var forbidden in new[] { "headline_prediction", "prediction", "qualified", "kelly_stake", "odds", "gate_outcome", "best_bet" })
            json.Should().NotContain($"\"{forbidden}\"");
        json.Should().Contain("\"h2h\"");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EvidenceShowsMeasuredCounterEvidenceFirstAndNeverExceedsFive(string language)
    {
        var rules = Enumerable.Range(1, 7).Select(count => new RuleResult("btts_confirm_h2h_rate", RuleResult.Confirm, true,
            $"BTTS in {count * 10}% of last 10 H2H meetings")).ToList();
        rules.Add(new("btts_veto_clean_sheets", RuleResult.Veto, true, "Away kept 3 clean sheets in their last 5 away matches"));
        rules.Add(new("btts_ai_agrees", RuleResult.Confirm, true, "AI votes yes"));
        var result = MatchBriefingFactory.Create(Match(rules), language);
        var evidence = result.GoalProfiles.First().Evidence;
        evidence.Should().HaveCount(5);
        evidence[0].Direction.Should().Be("against");
        evidence[0].Text.Should().Contain("Away").And.Contain("3").And.Contain("5");
        evidence.Should().OnlyContain(item => item.Text.Length <= 260 && !item.Text.Contains("%") && !item.Text.Contains("votes"));
        evidence.Should().NotContain(item => item.Text.Contains("not selected") || item.Text.Contains("verworfen"));
    }

    [Fact]
    public void MissingModelsAreNotZeroProbabilityOrInventedDataChecks()
    {
        var result = MatchBriefingFactory.Create(new MatchAnalysis { Prediction = new() }, "en");
        result.DataStatus.Should().Be("unavailable");
        result.Outcomes.Should().OnlyContain(outcome => outcome.Probability == null);
        result.GoalProfiles.Should().OnlyContain(profile => profile.Probability == null);
        result.GoalProfiles.Should().OnlyContain(profile => profile.Evidence.Count == 1 && profile.Evidence[0].Direction == "context");
        MatchBriefingFactory.ValidProbability(double.NaN).Should().BeFalse();
        MatchBriefingFactory.ValidDistribution(.8, .2, .2).Should().BeFalse();
    }

    [Fact]
    public void UnsupportedRulesDoNotBecomeGenericApprovalClaims()
    {
        DecisionEvidenceFormatter.FormatAnalysis(new("btts_confirm_h2h_rate", RuleResult.Confirm, true,
            "One data check approved"), Match(), false).Should().BeNull();
    }
}
