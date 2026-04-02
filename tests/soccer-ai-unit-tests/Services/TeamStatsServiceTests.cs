using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Services;
using Xunit;

namespace soccer_ai_unit_tests.Services;

public class TeamStatsServiceTests
{
    private readonly TeamStatsService _sut;

    public TeamStatsServiceTests()
    {
        _sut = new TeamStatsService();
    }

    [Fact]
    public void Calculate_WhenNoMatches_ReturnsZeroedStats()
    {
        // Act
        var result = _sut.Calculate(1, new List<Fixture>(), true);

        // Assert
        result.AvgGoalsScoredLast3.Should().Be(0);
        result.BTTSRateLast3.Should().Be(0);
        result.WinRate.Should().Be(0);
        result.Possession.Should().Be(50.0); // Default possession
        result.Momentum.Should().Be(0);
    }

    [Fact]
    public void Calculate_WithSufficientMatches_CalculatesCorrectRates()
    {
        // Arrange
        var teamId = 1;
        var fixtures = new List<Fixture>
        {
            // All recent.
            new Fixture { HomeTeamId = teamId, HomeGoal = 3, AwayGoal = 1, Date = DateTimeOffset.UtcNow.AddDays(-1), HomeBallPossession = 60 }, // Win, BTTS, Over2.5
            new Fixture { AwayTeamId = teamId, HomeGoal = 0, AwayGoal = 2, Date = DateTimeOffset.UtcNow.AddDays(-2), AwayBallPossession = 55 }, // Win, CS
            new Fixture { HomeTeamId = teamId, HomeGoal = 1, AwayGoal = 1, Date = DateTimeOffset.UtcNow.AddDays(-3), HomeBallPossession = 50 }, // Draw, BTTS
            new Fixture { AwayTeamId = teamId, HomeGoal = 2, AwayGoal = 0, Date = DateTimeOffset.UtcNow.AddDays(-4), AwayBallPossession = 45 }  // Loss
        };

        // Act
        var result = _sut.Calculate(teamId, fixtures, true);

        // Assert
        // Last 3 matches scored: 3 + 2 + 1 = 6. Avg = 2.0
        result.AvgGoalsScoredLast3.Should().Be(2.0);

        // BTTS in last 3: Match 1 & 3. Rate = 2/3 = 0.67
        result.BTTSRateLast3.Should().Be(0.67);

        // Over 2.5 in last 3: Match 1. Rate = 1/3 = 0.33
        result.Over25RateLast3.Should().Be(0.33);

        // Win Rate in last 7: Match 1 & 2. Total 4 matches. Rate = 2/4 = 0.5
        result.WinRate.Should().Be(0.5);

        // Possession: (60 + 55 + 50 + 45) / 4 = 52.5
        result.Possession.Should().Be(52.5);
    }
}
