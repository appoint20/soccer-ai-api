using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.MlNet;
using SoccerAi.Infrastructure.MlNet.Models;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.MlNet;

public class MlTrainingDataBuilderTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DixonColesModel _dcModel;
    private readonly MlTrainingDataBuilder _sut;
    private readonly LeagueVolatilityService _volatility = new();

    public MlTrainingDataBuilderTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _dcModel = new DixonColesModel(
            _dbContext,
            Microsoft.Extensions.Options.Options.Create(new DixonColesOptions()),
            new Mock<ILogger<DixonColesModel>>().Object);

        _sut = new MlTrainingDataBuilder(new Mock<ILogger<MlTrainingDataBuilder>>().Object);
    }

    private static Fixture MakeFixture(int id, DateTimeOffset date, int home, int away,
        int homeGoals, int awayGoals, int leagueId = 39) => new()
    {
        Id = id,
        LeagueId = leagueId,
        HomeTeamId = home,
        AwayTeamId = away,
        Status = "FT",
        Date = date,
        HomeGoal = homeGoals,
        AwayGoal = awayGoals,
        HomeElo = 1550,
        AwayElo = 1480,
        HomeWinOdds = 2.0,
        DrawOdds = 3.4,
        AwayWinOdds = 3.8,
        Over25Odds = 1.9,
        Under25Odds = 1.9,
        BttsYesOdds = 1.8
    };

    /// <summary>Round-robin style history so every team accumulates matches.</summary>
    private async Task<List<Fixture>> SeedSeasonAsync(int matches)
    {
        var fixtures = new List<Fixture>();
        int[] teams = [100, 200, 300, 400];
        var date = new DateTimeOffset(2025, 8, 1, 15, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < matches; i++)
        {
            var home = teams[i % 4];
            var away = teams[(i + 1) % 4];
            fixtures.Add(MakeFixture(i + 1, date.AddDays(i * 3), home, away,
                homeGoals: i % 3, awayGoals: (i + 1) % 2));
        }

        _dbContext.Fixtures.AddRange(fixtures);
        await _dbContext.SaveChangesAsync();
        return fixtures.OrderBy(f => f.Date).ToList();
    }

    [Fact]
    public async Task ProducesOneRowPerFixturePerMarket()
    {
        var fixtures = await SeedSeasonAsync(40);

        var rows = await _sut.BuildAsync(fixtures, _dcModel, _volatility);

        rows.Should().NotBeEmpty();
        rows.Count.Should().Be(rows.Select(r => r.FixtureId).Distinct().Count() * 5,
            "each eligible fixture produces exactly 5 market rows");

        var byFixture = rows.GroupBy(r => r.FixtureId).First();
        byFixture.Select(r => r.Market).Should().BeEquivalentTo(MarketTrainingRow.Markets.All);
    }

    [Fact]
    public async Task EarlyFixtures_WithoutHistory_ProduceNoRows()
    {
        var fixtures = await SeedSeasonAsync(40);

        var rows = await _sut.BuildAsync(fixtures, _dcModel, _volatility);

        // The first fixtures cannot have 5 prior matches per team + 10 league matches.
        var earliestFixtureWithRows = rows.Min(r => r.FixtureId);
        earliestFixtureWithRows.Should().BeGreaterThan(10,
            "rows require pre-match history for both teams and the league minimum");
    }

    [Fact]
    public async Task Labels_MatchActualOutcomes()
    {
        var fixtures = await SeedSeasonAsync(40);

        var rows = await _sut.BuildAsync(fixtures, _dcModel, _volatility);
        var byId = fixtures.ToDictionary(f => (float)f.Id);

        foreach (var row in rows)
        {
            var f = byId[row.FixtureId];
            var total = f.HomeGoal + f.AwayGoal;
            var expected = row.Market switch
            {
                MarketTrainingRow.Markets.Over25 => total > 2,
                MarketTrainingRow.Markets.Btts => f.HomeGoal > 0 && f.AwayGoal > 0,
                MarketTrainingRow.Markets.Goals23 => total is 2 or 3,
                MarketTrainingRow.Markets.HomeWin => f.HomeGoal > f.AwayGoal,
                MarketTrainingRow.Markets.AwayWin => f.AwayGoal > f.HomeGoal,
                _ => throw new InvalidOperationException($"unknown market {row.Market}")
            };
            row.Label.Should().Be(expected, "label for {0} of fixture {1}", row.Market, f.Id);
        }
    }

    [Fact]
    public async Task Features_AreWithinSaneRanges()
    {
        var fixtures = await SeedSeasonAsync(40);

        var rows = await _sut.BuildAsync(fixtures, _dcModel, _volatility);

        foreach (var row in rows)
        {
            row.DcProb.Should().BeInRange(0, 1);
            row.MarketProb.Should().BeInRange(0, 1);
            row.HomeForm.Should().BeInRange(0, 1);
            row.AwayForm.Should().BeInRange(0, 1);
            row.HomeRestDays.Should().BeInRange(0, 14);
            row.AwayRestDays.Should().BeInRange(0, 14);
            row.EloDiff.Should().Be(70, "Elo snapshot is 1550 vs 1480");
            row.LeagueVolatility.Should().BeInRange(0, 1);
            row.HasMarketProb.Should().Be(row.Market == MarketTrainingRow.Markets.Goals23 ? 0f : 1f,
                "goals23 has no odds market; all other markets have seeded odds");
        }
    }

    [Fact]
    public async Task MarketProb_ForGoals23_IsNeutral()
    {
        var fixtures = await SeedSeasonAsync(40);

        var rows = await _sut.BuildAsync(fixtures, _dcModel, _volatility);

        rows.Where(r => r.Market == MarketTrainingRow.Markets.Goals23)
            .Should().OnlyContain(r => r.MarketProb == 0.5f && r.HasMarketProb == 0f);
    }
}
