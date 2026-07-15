using FluentAssertions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;

namespace soccer_ai_unit_tests.Services;

public class AnalysisSnapshotSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesResponseFields()
    {
        var original = new MatchAnalysis
        {
            Id = 42,
            Date = new DateTimeOffset(2026, 7, 14, 18, 30, 0, TimeSpan.Zero),
            Time = new TimeSpan(18, 30, 0),
            League = "Premier League",
            HomeTeam = "Arsenal",
            AwayTeam = "Chelsea",
            OddsHomeWin = 2.1,
            OddsOver25 = 1.85,
            Trap = new TrapDecision { IsTrap = true, Reason = "test trap" },
            Ai = new AiAnalysisDto { Recommendation = "Over 2.5", Confidence = 71 }
        };

        var json = AnalysisSnapshotSerializer.Serialize(original);
        var restored = AnalysisSnapshotSerializer.Deserialize(json);

        restored.Should().NotBeNull();
        restored!.Id.Should().Be(42);
        restored.Date.Should().Be(original.Date);
        restored.League.Should().Be("Premier League");
        restored.HomeTeam.Should().Be("Arsenal");
        restored.AwayTeam.Should().Be("Chelsea");
        restored.OddsHomeWin.Should().Be(2.1);
        restored.OddsOver25.Should().Be(1.85);
        restored.Trap.IsTrap.Should().BeTrue();
        restored.Trap.Reason.Should().Be("test trap");
        restored.Ai!.Recommendation.Should().Be("Over 2.5");
        restored.Ai.Confidence.Should().Be(71);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsNull()
    {
        AnalysisSnapshotSerializer.Deserialize(null).Should().BeNull();
        AnalysisSnapshotSerializer.Deserialize("").Should().BeNull();
        AnalysisSnapshotSerializer.Deserialize("   ").Should().BeNull();
    }

    [Fact]
    public void Deserialize_CorruptJson_ReturnsNullInsteadOfThrowing()
    {
        AnalysisSnapshotSerializer.Deserialize("{not valid json!").Should().BeNull();
    }
}
