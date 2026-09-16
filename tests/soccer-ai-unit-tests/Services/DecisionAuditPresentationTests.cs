using System.Text.Json;
using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Decisions;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// What the decision protocol shows the reader, and whether the head-to-head
/// block reaches a client at all.
/// </summary>
public class DecisionAuditPresentationTests
{
    // ── Evidence: the "n/a; n/a" the app was printing ────────────────────────

    private static RuleResult Rule(string evidence) =>
        new("r", RuleResult.Confirm, false, evidence);

    [Fact]
    public void EvidenceWithNothingMeasured_BecomesEmptyRatherThanAPlaceholder()
    {
        var normalised = ConfluenceRuleEngine.NormaliseEvidence(Rule("n/a; n/a"));

        // Empty is the signal the client turns into "No data for this check".
        // Printing "n/a; n/a" showed a placeholder as though it were a finding.
        normalised.Evidence.Should().BeEmpty();
    }

    /// <summary>
    /// The half that WAS measured is the whole point — dropping the pair would
    /// discard a real observation along with the placeholder.
    /// </summary>
    [Fact]
    public void OnlyTheUnmeasuredHalfIsDropped()
    {
        var normalised = ConfluenceRuleEngine.NormaliseEvidence(
            Rule("n/a; Conceded in 2/3 of last 3 away matches"));

        normalised.Evidence.Should().Be("Conceded in 2/3 of last 3 away matches");
    }

    /// <summary>
    /// A described absence explains itself and is worth reading; only the bare
    /// marker is noise.
    /// </summary>
    [Fact]
    public void DescriptiveAbsencesSurvive()
    {
        var normalised = ConfluenceRuleEngine.NormaliseEvidence(
            Rule("No head-to-head history"));

        normalised.Evidence.Should().Be("No head-to-head history");
    }

    [Fact]
    public void RealEvidenceIsUntouched()
    {
        const string evidence = "Scored in 3/3 of last 3 home matches; Scored in 3/3 of last 3 away matches";

        ConfluenceRuleEngine.NormaliseEvidence(Rule(evidence)).Evidence.Should().Be(evidence);
    }

    [Fact]
    public void ThePlaceholderConstantMatchesTheDefaultsItGuards()
    {
        // If SignalValue's defaults ever stop using this constant the
        // normaliser silently stops matching them, and "n/a; n/a" returns.
        SignalValue.Unavailable(SignalValue.NotAvailable).Label
            .Should().Be(SignalValue.NotAvailable);
    }

    // ── Head-to-head: the key nobody was reading ─────────────────────────────

    /// <summary>
    /// `H2H` under SnakeCaseLower serialises as "h2_h", which is what the app
    /// was never looking for. The explicit name fixes the contract.
    /// </summary>
    [Fact]
    public void H2HIsPublishedUnderAReadableKey()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var json = JsonSerializer.Serialize(
            new MatchAnalysis { H2H = new HeadToHeadModel { MatchesAnalyzed = 5 } }, options);

        json.Should().Contain("\"h2h\"");
        json.Should().NotContain("\"h2_h\"");
    }

    /// <summary>
    /// Every snapshot already in the database was written with the PascalCase
    /// "H2H". Renaming the property must not orphan them, or the section stays
    /// blank until the whole board is recomputed.
    /// </summary>
    [Fact]
    public void SnapshotsWrittenWithTheOldKeyStillDeserialize()
    {
        const string legacy = """
            {"Id":26414,"H2H":{"MatchesAnalyzed":5,"HomeWinRate":0.6,"AwayWinRate":0.4,
             "Over25Rate":0.6,"AvgTotalGoals":2.4,"IsValid":true}}
            """;

        var analysis = AnalysisSnapshotSerializer.Deserialize(legacy);

        analysis.Should().NotBeNull();
        analysis!.H2H.Should().NotBeNull();
        analysis.H2H!.MatchesAnalyzed.Should().Be(5);
        analysis.H2H.AvgTotalGoals.Should().Be(2.4);
    }
}
