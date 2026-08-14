using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Persistence;

/// <summary>
/// Applies every SQLite migration to a fresh database and touches every table.
///
/// This exists because a migration missing its [Migration] and [DbContext]
/// attributes is <em>invisible to EF</em>. It compiles, it is committed, it
/// looks applied — and it silently does nothing. The gap only surfaces much
/// later as "SQLite Error 1: no such table", usually somewhere unrelated to the
/// change that introduced it.
/// </summary>
public class MigrationCompletenessTests
{
    [Fact]
    public async Task EveryEntityHasATableAfterMigrating()
    {
        // In-memory SQLite keeps the schema for as long as the connection is
        // open, which is exactly the lifetime of this test.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();

        // Counting forces a real query per table. A missing one throws here
        // rather than in production.
        var counts = new Dictionary<string, Func<Task<int>>>
        {
            [nameof(db.Teams)] = () => db.Teams.CountAsync(),
            [nameof(db.Fixtures)] = () => db.Fixtures.CountAsync(),
            [nameof(db.FixtureAnalyses)] = () => db.FixtureAnalyses.CountAsync(),
            [nameof(db.FixtureOddsQuotes)] = () => db.FixtureOddsQuotes.CountAsync(),
            [nameof(db.Combinations)] = () => db.Combinations.CountAsync(),
            [nameof(db.Users)] = () => db.Users.CountAsync(),
            [nameof(db.BacktestReports)] = () => db.BacktestReports.CountAsync(),
            [nameof(db.SyncStates)] = () => db.SyncStates.CountAsync(),
            [nameof(db.PublishedTickets)] = () => db.PublishedTickets.CountAsync(),
            [nameof(db.PublishedTicketLegs)] = () => db.PublishedTicketLegs.CountAsync(),
            [nameof(db.ModelForecasts)] = () => db.ModelForecasts.CountAsync()
        };

        foreach (var (name, count) in counts)
        {
            var act = async () => await count();
            await act.Should().NotThrowAsync($"{name} must have a table created by a migration");
        }
    }

    [Fact]
    public async Task NoEntityIsMissingFromTheAssertionsAbove()
    {
        // Guards the test itself: adding an entity without adding it to the
        // dictionary above would leave the new table unchecked.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Model.GetEntityTypes().Should().HaveCount(11,
            "every entity must also be asserted in EveryEntityHasATableAfterMigrating");
    }
}
