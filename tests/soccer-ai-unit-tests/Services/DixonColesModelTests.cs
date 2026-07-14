using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

public class DixonColesModelTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DixonColesModel _sut;

    private const int LeagueId = 1;
    private const int HomeTeamId = 100;
    private const int AwayTeamId = 200;

    public DixonColesModelTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _sut = new DixonColesModel(
            _dbContext,
            Microsoft.Extensions.Options.Options.Create(new DixonColesOptions()),
            new Mock<ILogger<DixonColesModel>>().Object);
    }

    private async Task SeedMatchesAsync(
        int count,
        DateTimeOffset newestDate,
        bool isCurrentSeason = true,
        int homeGoals = 2,
        int awayGoals = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = _dbContext.Fixtures.Local.Count + i + 1,
                LeagueId = LeagueId,
                HomeTeamId = i % 2 == 0 ? HomeTeamId : 999,
                AwayTeamId = i % 2 != 0 ? AwayTeamId : 888,
                Status = "FT",
                IsCurrentSeason = isCurrentSeason,
                Date = newestDate.AddDays(-i - 1),
                HomeGoal = homeGoals,
                AwayGoal = awayGoals
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task WhenNotEnoughMatches_ReturnsNull()
    {
        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, DateTimeOffset.UtcNow);

        result.Should().BeNull("fewer than MinLeagueMatches finished matches exist");
    }

    [Fact]
    public async Task WithSufficientMatches_AllMarketsComeFromOneValidMatrix()
    {
        var matchDate = DateTimeOffset.UtcNow;
        await SeedMatchesAsync(12, matchDate);

        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        result.Should().NotBeNull();
        result!.HomeExpectedGoals.Should().BeGreaterThan(0);
        result.AwayExpectedGoals.Should().BeGreaterThan(0);

        (result.HomeWin + result.Draw + result.AwayWin)
            .Should().BeApproximately(1.0, 1e-6, "1X2 comes from a renormalized matrix");

        result.Over25.Should().BeGreaterThan(0).And.BeLessThan(1);
        result.BothTeamScoredGoal.Should().BeGreaterThan(0).And.BeLessThan(1);
        result.TwoToThreeGoals.Should().BeGreaterThan(0).And.BeLessThan(1);
    }

    [Fact]
    public async Task UsesOldSeasons_IsCurrentSeasonHardCutIsGone()
    {
        // ALL matches flagged as previous season — the old implementation
        // (WHERE IsCurrentSeason) would have returned null here.
        var matchDate = DateTimeOffset.UtcNow;
        await SeedMatchesAsync(12, matchDate, isCurrentSeason: false);

        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        result.Should().NotBeNull("all seasons must be usable via time decay");
    }

    [Fact]
    public async Task ExcludesMatchesOnOrAfterFixtureDate()
    {
        // 12 matches, but they are all AFTER the fixture being analyzed.
        var matchDate = DateTimeOffset.UtcNow;
        await SeedMatchesAsync(12, matchDate.AddDays(60));

        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        result.Should().BeNull("future results must never leak into the calculation");
    }

    [Fact]
    public async Task TimeDecay_RecentFormOutweighsStaleForm()
    {
        // Two identical leagues; in league A the high-scoring matches are
        // recent, in league B they are two years old. Recent goals must
        // produce higher expected totals.
        var matchDate = DateTimeOffset.UtcNow;

        async Task SeedLeague(int leagueId, int recentGoals, int staleGoals)
        {
            var id = leagueId * 1000;
            for (var i = 0; i < 10; i++)
            {
                _dbContext.Fixtures.Add(new Fixture
                {
                    Id = id + i,
                    LeagueId = leagueId,
                    HomeTeamId = i % 2 == 0 ? HomeTeamId : 999,
                    AwayTeamId = i % 2 != 0 ? AwayTeamId : 888,
                    Status = "FT",
                    Date = matchDate.AddDays(-i - 1),          // recent
                    HomeGoal = recentGoals,
                    AwayGoal = recentGoals
                });
                _dbContext.Fixtures.Add(new Fixture
                {
                    Id = id + 100 + i,
                    LeagueId = leagueId,
                    HomeTeamId = i % 2 == 0 ? HomeTeamId : 999,
                    AwayTeamId = i % 2 != 0 ? AwayTeamId : 888,
                    Status = "FT",
                    Date = matchDate.AddDays(-730 - i),        // ~2 years old
                    HomeGoal = staleGoals,
                    AwayGoal = staleGoals
                });
            }
            await _dbContext.SaveChangesAsync();
        }

        await SeedLeague(leagueId: 2, recentGoals: 3, staleGoals: 0);
        await SeedLeague(leagueId: 3, recentGoals: 0, staleGoals: 3);

        var recentHigh = await _sut.CalculateProbabilitiesAsync(2, HomeTeamId, AwayTeamId, matchDate);
        var staleHigh = await _sut.CalculateProbabilitiesAsync(3, HomeTeamId, AwayTeamId, matchDate);

        recentHigh.Should().NotBeNull();
        staleHigh.Should().NotBeNull();

        (recentHigh!.HomeExpectedGoals + recentHigh.AwayExpectedGoals)
            .Should().BeGreaterThan(staleHigh!.HomeExpectedGoals + staleHigh.AwayExpectedGoals,
                "recent matches carry exponentially more weight");
    }

    [Fact]
    public async Task IgnoresUnfinishedMatches()
    {
        var matchDate = DateTimeOffset.UtcNow;
        await SeedMatchesAsync(9, matchDate); // one short of MinLeagueMatches

        // 5 scheduled (not finished) matches must not push it over the minimum.
        for (var i = 0; i < 5; i++)
        {
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = 500 + i,
                LeagueId = LeagueId,
                HomeTeamId = HomeTeamId,
                AwayTeamId = AwayTeamId,
                Status = "NS",
                Date = matchDate.AddDays(-1),
                HomeGoal = 0,
                AwayGoal = 0
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        result.Should().BeNull("only Status == FT matches count");
    }
}
