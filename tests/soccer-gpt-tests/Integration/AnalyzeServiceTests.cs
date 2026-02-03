using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Services;
using soccer_gpt_infrastructure.Persistence;

namespace soccer_gpt_tests.Integration;

public class AnalyzeServiceTests
{
    [Fact]
    public async Task AnalyzeUpcomingAsync_WithNoFixtures_ReturnsEmptyList()
    {
        var (_, analyzeService) = CreateServices();
        
        var result = await analyzeService.AnalyzeUpcomingAsync(DateTime.Today);
        
        Assert.Empty(result);
    }

    [Fact]
    public async Task AnalyzeUpcomingAsync_WithFixtures_ReturnsAnalysisForEach()
    {
        var (dbContext, analyzeService) = CreateServices();
        var targetDate = DateTime.Today.AddDays(1);
        await SeedTestData(dbContext, targetDate);

        var result = await analyzeService.AnalyzeUpcomingAsync(targetDate);

        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.NotEmpty(r.HomeTeam));
        Assert.All(result, r => Assert.NotEmpty(r.AwayTeam));
    }

    [Fact]
    public async Task AnalyzeUpcomingAsync_WithPagination_RespectsLimits()
    {
        var (dbContext, analyzeService) = CreateServices();
        var targetDate = DateTime.Today.AddDays(1);
        await SeedTestData(dbContext, targetDate);

        var result = await analyzeService.AnalyzeUpcomingAsync(targetDate, offset: 0, limit: 1);

        Assert.True(result.Count <= 1);
    }

    private static (ApplicationDbContext, AnalyzeService) CreateServices()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new ApplicationDbContext(options);
        var teamStatsService = new TeamStatsService();
        var leagueStatsService = new LeagueStatsService();
        var poissonService = new PoissonService();
        var h2hService = new HeadToHeadService();
        var monteCarloService = new MonteCarloService();
        var qualificationService = new QualificationService();
        var decisionBuilderService = new DecisionBuilderService();

        var analyzeService = new AnalyzeService(
            dbContext, 
            teamStatsService, 
            leagueStatsService, 
            poissonService,
            h2hService,
            monteCarloService,
            qualificationService,
            decisionBuilderService);

        return (dbContext, analyzeService);
    }

    private static async Task SeedTestData(ApplicationDbContext db, DateTime fixtureDate)
    {
        var arsenal = new Team { Id = 1, Name = "Arsenal" };
        var chelsea = new Team { Id = 2, Name = "Chelsea" };

        db.Teams.AddRange(arsenal, chelsea);

        db.Matches.AddRange(
            TestDataFactory.CreateMatch(1, arsenal, chelsea, 2, 1, fixtureDate.AddDays(-7)),
            TestDataFactory.CreateMatch(2, chelsea, arsenal, 1, 1, fixtureDate.AddDays(-14)),
            TestDataFactory.CreateMatch(3, arsenal, chelsea, 3, 0, fixtureDate.AddDays(-21))
        );

        db.Fixtures.Add(new Fixture
        {
            Id = 1,
            HomeName = "Arsenal",
            AwayName = "Chelsea",
            Date = fixtureDate,
            Time = new TimeSpan(15, 0, 0),
            LeagueName = "Premier League"
        });

        await db.SaveChangesAsync();
    }
}
