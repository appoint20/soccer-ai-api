using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Services.Analysis;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The client prints the trap reason verbatim, so a flag without one leaves a
/// bare warning on screen. These pin the guarantee that the flag always arrives
/// explained — and that the explanation is never invented.
/// </summary>
public class TrapReasonTests
{
    private static Fixture Fixture() => new()
    {
        Id = 100,
        Date = DateTimeOffset.UtcNow,
        Status = "NS",
        HomeTeamId = 1,
        AwayTeamId = 2,
        LeagueId = 39,
    };

    private static FixtureAnalysisResult Analysis(
        TrapDecision trap, StrategicSignals? signals = null) => new()
        {
            FixtureId = 100,
            TeamStats = new TeamStatsResponse(),
            Models = new StatisticalModels(),
            H2H = HeadToHeadModel.Empty,
            Decisions = new DecisionServiceResult { Trap = trap },
            LeagueName = "Premier League",
            Prediction = new WeightedPrediction(),
            Signals = signals,
        };

    private static MatchAnalysis Map(FixtureAnalysisResult analysis, AiAnalysisDto? ai = null) =>
        AnalysisResponseMapper.MapToResponse(
            Fixture(), analysis,
            new Team { ApiId = 1, Name = "Home" },
            new Team { ApiId = 2, Name = "Away" },
            ai);

    private static StrategicSignals SignalsWithTrap(string label, bool flag = true) =>
        new() { Market = new MarketSignals { Trap = SignalValue.Of(6, flag, label) } };

    [Fact]
    public void AiReasonIsUsed_WhenTheModelSuppliesOne()
    {
        var result = Map(
            Analysis(new TrapDecision()),
            new AiAnalysisDto { IsTrap = true, TrapReason = "Price implies 62%; the model has 41%." });

        result.Trap.IsTrap.Should().BeTrue();
        result.Trap.Reason.Should().Be("Price implies 62%; the model has 41%.");
    }

    [Fact]
    public void StatisticalReasonIsUsed_WhenTheModelFlagsWithoutOne()
    {
        var result = Map(
            Analysis(new TrapDecision { IsTrap = true, Reason = "Market favors the worse-ranked side." }),
            new AiAnalysisDto { IsTrap = true, TrapReason = "   " });

        result.Trap.Reason.Should().Be("Market favors the worse-ranked side.");
    }

    [Fact]
    public void SignalEvidenceIsUsed_WhenNeitherSourceExplainsItself()
    {
        var result = Map(Analysis(
            new TrapDecision { IsTrap = true },
            SignalsWithTrap("Market favors the side ranked 6 places WORSE — classic trap pattern")));

        result.Trap.Reason.Should().Contain("6 places WORSE");
    }

    [Fact]
    public void ReasonIsNeverEmpty_WhenTheFlagIsSet()
    {
        var result = Map(Analysis(new TrapDecision { IsTrap = true }));

        result.Trap.IsTrap.Should().BeTrue();
        result.Trap.Reason.Should().NotBeNullOrWhiteSpace();
        result.Trap.Reason.Should().Contain("no supporting detail",
            "an honest non-explanation beats a fabricated rationale");
    }

    [Fact]
    public void FiredSignalsAreReportedAsStructuredEvidence()
    {
        var result = Map(Analysis(
            new TrapDecision { IsTrap = true, Reason = "Trap." },
            SignalsWithTrap("Market favors the side ranked 6 places WORSE")));

        var signal = result.Trap.Signals.Should().ContainSingle().Subject;
        signal.Id.Should().Be("market_favors_worse_side");
        signal.Evidence.Should().Contain("6 places WORSE");
    }

    [Fact]
    public void UnflaggedSignalsAreNotListedAsEvidence()
    {
        var result = Map(Analysis(
            new TrapDecision { IsTrap = true, Reason = "Trap." },
            SignalsWithTrap("Odds aligned with table logic", flag: false)));

        result.Trap.Signals.Should().BeEmpty(
            "an unflagged signal is the absence of evidence, not support for the warning");
    }

    [Fact]
    public void NoTrapMeansNothingToExplain()
    {
        var result = Map(Analysis(new TrapDecision()));

        result.Trap.IsTrap.Should().BeFalse();
        result.Trap.Reason.Should().BeEmpty();
        result.Trap.Signals.Should().BeEmpty();
    }
}
