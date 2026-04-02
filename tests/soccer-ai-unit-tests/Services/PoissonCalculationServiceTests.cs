using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.Persistence;
using Xunit;

namespace soccer_ai_unit_tests.Services;

public class PoissonCalculationServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PoissonCalculationService _sut; // System Under Test

    public PoissonCalculationServiceTests()
    {
        // Setup InMemory Database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Setup Logger Mock
        var loggerMock = new Mock<ILogger<PoissonCalculationService>>();

        _sut = new PoissonCalculationService(_dbContext, loggerMock.Object);
    }

    [Fact]
    public async Task CalculateProbabilitiesAsync_WhenNotEnoughMatches_ReturnsNull()
    {
        // Act
        var result = await _sut.CalculateProbabilitiesAsync(1, 100, 200, DateTimeOffset.UtcNow);

        // Assert
        result.Should().BeNull("because there are fewer than 10 matches in the DB");
    }

    [Fact]
    public async Task CalculateProbabilitiesAsync_WithSufficientMatches_CalculatesCorrectly()
    {
        // Arrange
        var leagueId = 1;
        var homeTeamId = 100;
        var awayTeamId = 200;
        var matchDate = DateTimeOffset.UtcNow;

        // Add 12 dummy matches to satisfy the MinMatchesForCalculation = 10 requirement.
        for (int i = 0; i < 12; i++)
        {
            // We alternate combinations of home/away to give them stats.
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = i + 1,
                LeagueId = leagueId,
                HomeTeamId = i % 2 == 0 ? homeTeamId : 999,
                AwayTeamId = i % 2 != 0 ? awayTeamId : 888,
                Status = "FT",
                IsCurrentSeason = true,
                Date = matchDate.AddDays(-i - 1),
                HomeGoal = 2,
                AwayGoal = 1
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CalculateProbabilitiesAsync(leagueId, homeTeamId, awayTeamId, matchDate);

        // Assert
        result.Should().NotBeNull();
        result!.HomeExpectedGoals.Should().BeGreaterThan(0);
        result.AwayExpectedGoals.Should().BeGreaterThan(0);

        // Probability checks
        var totalResultProb = result.HomeWin + result.Draw + result.AwayWin;
        totalResultProb.Should().BeApproximately(1.0, 0.001, "Probabilities should sum to 100%");

        result.Over25.Should().BeGreaterThan(0).And.BeLessThan(1);
    }
}
