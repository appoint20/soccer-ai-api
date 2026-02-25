using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using soccer_gpt_application.Entities;
using soccer_gpt_infrastructure.Persistence;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_tests.Unit;

public class HistoricalDataServiceTests
{
    [Fact]
    public async Task GetTeamHistoryAsync_ReturnsMostRecentFixtures()
    {
        await using var db = CreateDbContext();
        SeedTeamsAndFixtures(db);

        var logger = new Mock<ILogger<HistoricalDataService>>();
        var sut = new HistoricalDataService(db, logger.Object);

        var beforeDate = DateTime.UtcNow.AddDays(1);
        var history = await sut.GetTeamHistoryAsync("Arsenal", 39, beforeDate, limit: 2);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].Date >= history[1].Date);
    }

    [Fact]
    public async Task GetAvailableDivisionsAsync_ReturnsLeagueCounts()
    {
        await using var db = CreateDbContext();
        SeedTeamsAndFixtures(db);

        var logger = new Mock<ILogger<HistoricalDataService>>();
        var sut = new HistoricalDataService(db, logger.Object);

        var divisions = await sut.GetAvailableDivisionsAsync();

        Assert.True(divisions.ContainsKey("E0"));
        Assert.True(divisions["E0"] >= 2);
    }

    [Fact]
    public async Task FindMatchAsync_UnknownTeam_ReturnsNull()
    {
        await using var db = CreateDbContext();
        SeedTeamsAndFixtures(db);

        var logger = new Mock<ILogger<HistoricalDataService>>();
        var sut = new HistoricalDataService(db, logger.Object);

        var result = await sut.FindMatchAsync("Unknown", "Chelsea", DateTime.UtcNow.AddDays(-1), 39);

        Assert.Null(result);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedTeamsAndFixtures(ApplicationDbContext db)
    {
        db.Teams.AddRange(
            new Team
            {
                ApiId = 1,
                Name = "Arsenal",
                LeagueId = 39,
                Rank = 1,
                Points = 50,
                GoalsFor = 45,
                GoalsAgainst = 20,
                GoalsDiff = 25,
                Played = 20,
                Win = 15,
                Draw = 5,
                Lose = 0,
                Form = "WWWWW",
                UpdatedAt = DateTime.UtcNow
            },
            new Team
            {
                ApiId = 2,
                Name = "Chelsea",
                LeagueId = 39,
                Rank = 2,
                Points = 48,
                GoalsFor = 40,
                GoalsAgainst = 22,
                GoalsDiff = 18,
                Played = 20,
                Win = 14,
                Draw = 6,
                Lose = 0,
                Form = "WWDWW",
                UpdatedAt = DateTime.UtcNow
            });

        db.Fixtures.AddRange(
            new Fixture
            {
                ApiId = 1001,
                HomeTeamId = 1,
                AwayTeamId = 2,
                LeagueId = 39,
                Date = DateTime.UtcNow.AddDays(-8),
                Status = "FT",
                HomeGoal = 2,
                AwayGoal = 1
            },
            new Fixture
            {
                ApiId = 1002,
                HomeTeamId = 2,
                AwayTeamId = 1,
                LeagueId = 39,
                Date = DateTime.UtcNow.AddDays(-4),
                Status = "FT",
                HomeGoal = 0,
                AwayGoal = 1
            });

        db.SaveChanges();
    }
}
