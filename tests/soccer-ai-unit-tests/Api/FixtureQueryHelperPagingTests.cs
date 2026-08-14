using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Helpers;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The window has to be applied in the query, not after it. This helper
/// previously paged only when a page and a page size were both supplied, so the
/// documented call loaded every fixture on the date and analyzed each one.
/// </summary>
public class FixtureQueryHelperPagingTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FixtureQueryHelper _sut;

    private static readonly DateTimeOffset Day = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    public FixtureQueryHelperPagingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _sut = new FixtureQueryHelper(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Sixty fixtures on one day — the case that took 90 seconds unpaged.</summary>
    private async Task SeedAsync(int count, TimeSpan? spacing = null)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Fixtures.Add(new Fixture
            {
                Id = i + 1,
                ApiId = 1000 + i,
                HomeTeamId = 1,
                AwayTeamId = 2,
                LeagueId = 39,
                Date = Day.AddHours(12) + (spacing.HasValue ? spacing.Value * i : TimeSpan.Zero)
            });
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Returns_only_the_requested_window_of_a_busy_day()
    {
        await SeedAsync(60, TimeSpan.FromMinutes(5));

        var (fixtures, _, total) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 20, offset: 0);

        fixtures.Should().HaveCount(20);
        total.Should().Be(60, "total reports the whole day, not the page");
    }

    [Fact]
    public async Task Offset_advances_to_the_next_window()
    {
        await SeedAsync(60, TimeSpan.FromMinutes(5));

        var (first, _, _) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 20, offset: 0);
        var (second, _, _) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 20, offset: 20);

        second.Should().HaveCount(20);
        first.Select(f => f.Id).Should().NotIntersectWith(second.Select(f => f.Id));
    }

    /// <summary>
    /// Kickoffs are shared across a matchday, so ordering by date alone leaves
    /// ties unordered — a row could repeat on one page and vanish from the next.
    /// </summary>
    [Fact]
    public async Task Pages_do_not_overlap_when_every_fixture_shares_a_kickoff()
    {
        await SeedAsync(30);

        var seen = new List<int>();
        for (var offset = 0; offset < 30; offset += 10)
        {
            var (page, _, _) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 10, offset: offset);
            seen.AddRange(page.Select(f => f.Id));
        }

        seen.Should().HaveCount(30);
        seen.Should().OnlyHaveUniqueItems("a stable sort must not repeat or drop rows across pages");
    }

    [Fact]
    public async Task Final_page_is_short_rather_than_padded()
    {
        await SeedAsync(25, TimeSpan.FromMinutes(5));

        var (page, _, total) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 10, offset: 20);

        page.Should().HaveCount(5);
        total.Should().Be(25);
    }

    [Fact]
    public async Task Offset_beyond_the_end_returns_an_empty_page_with_the_real_total()
    {
        await SeedAsync(10, TimeSpan.FromMinutes(5));

        var (page, _, total) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 20, offset: 500);

        page.Should().BeEmpty();
        total.Should().Be(10);
    }

    [Fact]
    public async Task Only_analyzed_narrows_both_the_page_and_the_total()
    {
        await SeedAsync(10, TimeSpan.FromMinutes(5));

        _db.FixtureAnalyses.Add(new FixtureAnalysis { FixtureId = 1, Lang = "en", SnapshotJson = "{}" });
        _db.FixtureAnalyses.Add(new FixtureAnalysis { FixtureId = 2, Lang = "en", SnapshotJson = "{}" });
        await _db.SaveChangesAsync(CancellationToken.None);

        var (page, _, total) = await _sut.GetFixturesWithTeamsAsync(
            Day, limit: 50, offset: 0, onlyAnalyzed: true);

        page.Should().HaveCount(2);
        total.Should().Be(2);
    }

    [Fact]
    public async Task Fixtures_on_other_days_are_excluded()
    {
        await SeedAsync(5, TimeSpan.FromMinutes(5));

        _db.Fixtures.Add(new Fixture
        {
            Id = 999, ApiId = 9999, HomeTeamId = 1, AwayTeamId = 2, LeagueId = 39,
            Date = Day.AddDays(1).AddHours(12)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var (page, _, total) = await _sut.GetFixturesWithTeamsAsync(Day, limit: 50, offset: 0);

        total.Should().Be(5);
        page.Select(f => f.Id).Should().NotContain(999);
    }
}
