using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

public class DecisionCheckEvidenceTests
{
    private static MatchAnalysis Match(params RuleResult[] rules) => new()
    {
        Id = 1, Date = DateTimeOffset.UtcNow.AddDays(1), HomeTeam = "Valladolid", AwayTeam = "Cordoba",
        Prediction = new PredictionResponse(),
        Provider = new ProviderPrediction
        {
            HeadToHead = .5, Goals = .53, Attack = .3,
            Home = new TeamRecentForm { Played = 5, Attack = .2, GoalsForAverage = .6, GoalsAgainstAverage = 1.2 },
            Away = new TeamRecentForm { Played = 5, Attack = .47, GoalsForAverage = 1.4, GoalsAgainstAverage = 2 }
        },
        DecisionAudit = new(2, [new("btts", .58, .5, true,
            rules.Count(rule => rule.Kind == RuleResult.Confirm && rule.Fired),
            rules.Count(rule => rule.Kind == RuleResult.Veto && rule.Fired),
            !rules.Any(rule => rule.Kind == RuleResult.Veto && rule.Fired), rules)
        {
            Selection = "BTTS",
            GateOutcome = rules.Any(rule => rule.Kind == RuleResult.Veto && rule.Fired) ? GateOutcome.Vetoed : GateOutcome.Qualified
        }], DateTimeOffset.UtcNow)
    };

    private static RuleResult Confirm(string rule, string text) => new(rule, RuleResult.Confirm, true, text);
    private static RuleResult Veto(string rule, string text) => new(rule, RuleResult.Veto, true, text);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void AtMostFivePointsWithActualRejectionReasonsFirst(string language)
    {
        var match = Match(
            Confirm("btts_confirm_both_score_venue", "Valladolid scored in 3 of their last 3 home matches; Cordoba scored in 3 of their last 3 away matches"),
            Confirm("btts_confirm_both_concede_venue", "Valladolid conceded in 3 of their last 3 home matches; Cordoba conceded in 3 of their last 3 away matches"),
            Confirm("btts_confirm_h2h_rate", "BTTS in 80% of last 5 H2H meetings"),
            Veto("btts_veto_clean_sheets", "Valladolid kept 0 clean sheets in their last 5 home matches; Cordoba kept 3 clean sheets in their last 5 away matches"),
            Veto("btts_veto_failed_to_score", "Valladolid failed to score in 3 of their last 5 home matches; Cordoba failed to score in 0 of their last 5 away matches"));
        DecisionExplanationPolicy.Refresh(match, language);
        var market = match.Presentation!.Markets.Single();

        market.Checks.Should().HaveCount(5);
        market.CheckOutcomes.Should().Equal(false, false, true, true, true);
        market.Checks[0].Should().Contain("Cordoba").And.Contain("3");
        market.Checks[1].Should().Contain("Valladolid").And.Contain("3");
        market.Checks.Should().OnlyContain(check => check.Length <= 260 && !DecisionExplanationPolicy.IsOpaqueCheck(check));
        string.Join(" ", market.Checks).Should().NotContain("0 of").And.NotContain("0 der");
        match.DecisionAudit!.Markets.Single().Rules.Should().HaveCount(5);
    }

    [Fact]
    public void GermanReasonsRemainSpecificWhenAiIsUnavailable()
    {
        var match = Match(Veto("btts_veto_failed_to_score",
            "Valladolid failed to score in 3 of their last 5 home matches"));
        DecisionExplanationPolicy.Refresh(match, "de");
        match.Presentation!.AiGenerated.Should().BeFalse();
        match.Presentation.Markets.Single().Checks[0].Should()
            .Contain("Valladolid blieb ohne Tor in 3 der letzten 5 Heimspiele")
            .And.Contain("nicht ausgewählt");
        string.Join(" ", match.Presentation.Markets.Single().Checks).Should()
            .NotContain("Datencheck").And.NotContain("Ausschlusskriterium").And.NotContain("%");
    }

    [Fact]
    public void ProviderRatingsAreNeverCalledGoalExpectations()
    {
        var match = Match();
        DecisionExplanationPolicy.Refresh(match, "de");
        var checks = match.Presentation!.Markets.Single().Checks;
        checks.Should().HaveCount(2);
        checks[0].Should().Contain("Valladolid erzielte 0,6 und kassierte 1,2").And.Contain("letzten 5 Spielen");
        checks[1].Should().Contain("Cordoba erzielte 1,4 und kassierte 2,0");
        string.Join(" ", checks).Should().NotContain("%").And.NotContain("Direkter Vergleich").And.NotContain("Angriffswert");
    }

    [Theory]
    [InlineData("en", "In 4 of the last 5 head-to-head meetings, both teams scored")]
    [InlineData("de", "In 4 der letzten 5 direkten Duelle trafen beide Teams")]
    public void HeadToHeadUsesMarketOccurrenceCountsNotProviderComparison(string language, string expected)
    {
        var match = Match(Confirm("btts_confirm_h2h_rate", "BTTS in 80% of last 5 H2H meetings"));
        DecisionExplanationPolicy.Refresh(match, language);
        match.Presentation!.Markets.Single().Checks[0].Should().Contain(expected);
    }

    [Theory]
    [InlineData("over25_confirm_both_venue_rates", "Cordoba: Over 2.5 in 60% of their last 5 away matches", "In 3 der letzten 5 Auswärtsspiele von Cordoba fielen mindestens drei Tore")]
    [InlineData("under25_confirm_both_venue_rates", "Cordoba: Under 2.5 in 80% of their last 5 away matches", "In 4 der letzten 5 Auswärtsspiele von Cordoba fielen höchstens zwei Tore")]
    [InlineData("draw_confirm_h2h_draws", "Draw in 40% of last 5 H2H meetings", "In 2 der letzten 5 direkten Duelle gab es ein Unentschieden")]
    [InlineData("goals23_confirm_moderate_totals", "Avg 2.4 total goals in last 5 matches; Avg 2.8 total goals in last 5 matches", "Spielen von Valladolid")]
    public void OtherMarketsExplainTheirOwnScoringCriteria(string ruleId, string evidence, string expected)
    {
        var match = Match();
        var text = DecisionEvidenceFormatter.Format(Confirm(ruleId, evidence), match, true);
        text.Should().Contain(expected).And.NotContain("%");
    }

    [Fact]
    public void InconsistentRateIsNotTurnedIntoAnInventedCount()
    {
        var rule = Confirm("btts_confirm_h2h_rate", "BTTS in 50% of last 5 H2H meetings");
        DecisionEvidenceFormatter.Format(rule, Match(), true).Should().NotContain("5").And.NotContain("3 der");
    }

    [Fact]
    public void UnfiredRulesAndDuplicateReasonsAreNotPublished()
    {
        var rule = Confirm("btts_confirm_h2h_rate", "BTTS in 80% of last 5 H2H meetings");
        var match = Match(rule, rule, new("btts_veto_failed_to_score", RuleResult.Veto, false, "Do not publish"));
        DecisionExplanationPolicy.Refresh(match, "en");
        var checks = match.Presentation!.Markets.Single().Checks;
        checks.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        checks.Should().NotContain(check => check.Contains("Do not publish"));
    }

    [Fact]
    public void EnglishNumbersDoNotDependOnMachineCulture()
    {
        var match = Match();
        DecisionExplanationPolicy.Refresh(match, "en");
        match.Presentation!.Markets.Single().Checks[0].Should().Contain("scored 0.6 and conceded 1.2");
    }

    [Fact]
    public void SparseEvidenceIsNotPaddedToFive()
    {
        var match = new MatchAnalysis
        {
            Id = 2, Date = DateTimeOffset.UtcNow.AddDays(1),
            DecisionAudit = new(2, [new("btts", .6, .5, true, 0, 0, false, [])
                { GateOutcome = GateOutcome.InsufficientConfirms }], DateTimeOffset.UtcNow)
        };
        DecisionExplanationPolicy.Refresh(match, "de");
        match.Presentation!.Markets.Single().Checks.Should().ContainSingle()
            .Which.Should().Contain("Heim-/Auswärtsform und direkte Duelle");
    }
}
