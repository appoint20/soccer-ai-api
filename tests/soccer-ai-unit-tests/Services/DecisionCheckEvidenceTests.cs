using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The panel used to report counts — "4 evidence checks support this selection"
/// — which is a number, not a reason. Every rule that fired now speaks for
/// itself and names its team, and each line carries its own mark so the app can
/// show what supports the call and what argues against it.
/// </summary>
public class DecisionCheckEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static MatchAnalysis Match(params RuleResult[] rules) => new()
    {
        Id = 1, Date = Now.AddHours(8), HomeTeam = "Barnsley", AwayTeam = "Preston",
        Prediction = new PredictionResponse(),
        DecisionAudit = new(2, [new("btts", .68, .6, true, rules.Count(r => r is { Kind: RuleResult.Confirm, Fired: true }),
            rules.Count(r => r is { Kind: RuleResult.Veto, Fired: true }), true, rules.ToList())
        {
            Selection = "BTTS", GateOutcome = GateOutcome.Qualified, AiAgrees = true
        }], Now)
    };

    private static RuleResult Confirm(string id, string evidence) => new(id, RuleResult.Confirm, true, evidence);
    private static RuleResult Veto(string id, string evidence) => new(id, RuleResult.Veto, true, evidence);

    [Fact]
    public void EveryFiredRuleBecomesItsOwnCheckInsteadOfACount()
    {
        var m = Match(
            Confirm("btts_confirm_both_score_venue", "Barnsley scored in 3 of their last 3 home matches"),
            Confirm("btts_confirm_h2h_rate", "Both teams scored in 80% of last 5 meetings"),
            Veto("btts_veto_failed_to_score", "Preston failed to score in 2 of their last 5 away matches"));

        DecisionExplanationPolicy.Refresh(m, "en");
        var checks = m.Presentation!.Markets.Single().Checks;

        checks.Should().HaveCount(6, "probability, three fired rules, the AI's view and the verdict");
        checks.Should().Contain(c => c.Contains("Barnsley scored in 3 of their last 3 home matches"));
        checks.Should().Contain(c => c.Contains("Preston failed to score"));
        checks.Should().NotContain(c => c.Contains("evidence checks support"),
            "a count is not a reason");
    }

    /// <summary>
    /// The mark is the server's, never the writer's: a veto has to keep reading
    /// as a mark against the market however its words are rewritten.
    /// </summary>
    [Fact]
    public void AConfirmIsMarkedForTheMarketAndAVetoAgainstIt()
    {
        var m = Match(
            Confirm("btts_confirm_both_score_venue", "Barnsley scored in 3 of their last 3 home matches"),
            Veto("btts_veto_failed_to_score", "Preston failed to score in 2 of their last 5 away matches"));

        DecisionExplanationPolicy.Refresh(m, "en");
        var market = m.Presentation!.Markets.Single();

        market.CheckOutcomes.Should().HaveCount(market.Checks.Count);
        var byText = market.Checks.Zip(market.CheckOutcomes).ToDictionary(p => p.First, p => p.Second);
        byText.Single(p => p.Key.Contains("Barnsley scored")).Value.Should().BeTrue();
        byText.Single(p => p.Key.Contains("Preston failed")).Value.Should().BeFalse();
    }

    [Fact]
    public void TheProbabilityCheckIsMarkedByWhetherItClearedItsFloor()
    {
        var m = Match(Confirm("btts_confirm_h2h_rate", "Both teams scored in 80% of last 5 meetings"));

        DecisionExplanationPolicy.Refresh(m, "en");
        var market = m.Presentation!.Markets.Single();

        market.Checks[0].Should().Contain("68%").And.Contain("60%");
        market.CheckOutcomes[0].Should().BeTrue();
    }

    /// <summary>
    /// The measured evidence is written in English and the writer produces the
    /// German. Until it does, German readers keep the localised counts rather
    /// than English sentences they cannot read.
    /// </summary>
    [Fact]
    public void GermanKeepsLocalisedCountsUntilTheWriterHasRewrittenThem()
    {
        var m = Match(Confirm("btts_confirm_both_score_venue", "Barnsley scored in 3 of their last 3 home matches"));

        DecisionExplanationPolicy.Refresh(m, "de");
        var checks = m.Presentation!.Markets.Single().Checks;

        checks.Should().NotContain(c => c.Contains("Barnsley scored"));
        checks.Should().Contain(c => c.Contains("Datencheck"));
        checks.Should().Contain(c => c.Contains("Ausschlusskriterium"));
    }

    /// <summary>A rule that did not fire is not evidence and is not shown.</summary>
    [Fact]
    public void RulesThatDidNotFireAreLeftOut()
    {
        var m = Match(
            Confirm("btts_confirm_both_score_venue", "Barnsley scored in 3 of their last 3 home matches"),
            new RuleResult("btts_confirm_both_concede_venue", RuleResult.Confirm, false, "Not fired"));

        DecisionExplanationPolicy.Refresh(m, "en");

        m.Presentation!.Markets.Single().Checks.Should().NotContain(c => c.Contains("Not fired"));
    }
}
