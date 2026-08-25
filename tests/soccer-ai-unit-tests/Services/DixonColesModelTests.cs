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

        _sut = NewModel();
    }

    /// <summary>
    /// A fresh model over the same database. The league-average cache is
    /// per-instance, so a test that seeds more fixtures mid-way has to ask a
    /// new instance rather than a warmed one.
    /// </summary>
    private DixonColesModel NewModel() => new(
        _dbContext,
        Microsoft.Extensions.Options.Options.Create(new DixonColesOptions()),
        new Mock<ILogger<DixonColesModel>>().Object);

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

    [Fact]
    public async Task PromotedTeam_WithNoHistoryInThisDivision_StillPriced()
    {
        // The reported failure: a club that just changed division. Its whole
        // record sits under the league it came from, so a league-filtered
        // lookup finds nothing and the fixture loses every market.
        var matchDate = DateTimeOffset.UtcNow;
        const int PriorLeagueId = 42;
        var id = 9000;

        // The target division has plenty of history — but none of it is the
        // promoted side's.
        for (var i = 0; i < 12; i++)
        {
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = id++,
                LeagueId = LeagueId,
                HomeTeamId = i % 2 == 0 ? AwayTeamId : 777,
                AwayTeamId = i % 2 == 0 ? 777 : AwayTeamId,
                Status = "FT",
                Date = matchDate.AddDays(-i - 1),
                HomeGoal = 2,
                AwayGoal = 1
            });
        }

        // The promoted club's record, all of it in the division below.
        for (var i = 0; i < 12; i++)
        {
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = id++,
                LeagueId = PriorLeagueId,
                HomeTeamId = i % 2 == 0 ? HomeTeamId : 555,
                AwayTeamId = i % 2 == 0 ? 555 : HomeTeamId,
                Status = "FT",
                Date = matchDate.AddDays(-i - 30),
                HomeGoal = 2,
                AwayGoal = 1
            });
        }

        await _dbContext.SaveChangesAsync();

        var result = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        result.Should().NotBeNull(
            "a promoted or relegated club must be priced from the division it came from");
    }

    [Fact]
    public async Task EstablishedTeam_IsUnaffectedByItsOtherCompetitions()
    {
        // The supplement must not leak into a club that already has a record
        // in this division — cup and continental results would otherwise move
        // every price in the league.
        var matchDate = DateTimeOffset.UtcNow;
        await SeedMatchesAsync(12, matchDate);

        var before = await _sut.CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        // A rout in another competition, recent enough to dominate if counted.
        for (var i = 0; i < 6; i++)
        {
            _dbContext.Fixtures.Add(new Fixture
            {
                Id = 7000 + i,
                LeagueId = 999,
                HomeTeamId = HomeTeamId,
                AwayTeamId = 4242,
                Status = "FT",
                Date = matchDate.AddHours(-i - 1),
                HomeGoal = 7,
                AwayGoal = 0
            });
        }
        await _dbContext.SaveChangesAsync();

        var after = await NewModel().CalculateProbabilitiesAsync(
            LeagueId, HomeTeamId, AwayTeamId, matchDate);

        before.Should().NotBeNull();
        after!.HomeExpectedGoals.Should().BeApproximately(before!.HomeExpectedGoals, 1e-9);
        after.AwayExpectedGoals.Should().BeApproximately(before.AwayExpectedGoals, 1e-9);
    }

    [Fact]
    public async Task CrossLeagueGoals_AreReadInThisDivisionsScoringEnvironment()
    {
        // Same club record — three goals a game — carried over from a division
        // where three goals a game is ordinary, versus one where it is
        // exceptional. The second must produce the higher expected goals.
        var matchDate = DateTimeOffset.UtcNow;

        async Task<double> ExpectedHomeGoalsFrom(int targetLeague, int priorLeague, int priorLeagueGoals)
        {
            var id = targetLeague * 10_000;

            // The division being priced: 12 matches, none of them the newcomer's.
            for (var i = 0; i < 12; i++)
            {
                _dbContext.Fixtures.Add(new Fixture
                {
                    Id = id++,
                    LeagueId = targetLeague,
                    HomeTeamId = i % 2 == 0 ? AwayTeamId : 777,
                    AwayTeamId = i % 2 == 0 ? 777 : AwayTeamId,
                    Status = "FT",
                    Date = matchDate.AddDays(-i - 1),
                    HomeGoal = 1,
                    AwayGoal = 1
                });
            }

            // The newcomer's record, plus enough of that division for its
            // baseline to be usable at all.
            for (var i = 0; i < 12; i++)
            {
                _dbContext.Fixtures.Add(new Fixture
                {
                    Id = id++,
                    LeagueId = priorLeague,
                    HomeTeamId = HomeTeamId,
                    AwayTeamId = 555,
                    Status = "FT",
                    Date = matchDate.AddDays(-i - 20),
                    HomeGoal = 3,
                    AwayGoal = 0
                });
                _dbContext.Fixtures.Add(new Fixture
                {
                    Id = id++,
                    LeagueId = priorLeague,
                    HomeTeamId = 666,
                    AwayTeamId = 555,
                    Status = "FT",
                    Date = matchDate.AddDays(-i - 20),
                    HomeGoal = priorLeagueGoals,
                    AwayGoal = 0
                });
            }
            await _dbContext.SaveChangesAsync();

            var result = await NewModel().CalculateProbabilitiesAsync(
                targetLeague, HomeTeamId, AwayTeamId, matchDate);

            result.Should().NotBeNull();
            return result!.HomeExpectedGoals;
        }

        // League 20: the newcomer's 3 goals a game were typical there.
        var fromHighScoringLeague = await ExpectedHomeGoalsFrom(20, priorLeague: 21, priorLeagueGoals: 3);
        // League 30: the same 3 goals a game stood out against a 1-goal norm.
        var fromLowScoringLeague = await ExpectedHomeGoalsFrom(30, priorLeague: 31, priorLeagueGoals: 1);

        fromLowScoringLeague.Should().BeGreaterThan(fromHighScoringLeague,
            "goals are worth what the division they were scored in makes them worth");
    }
}
