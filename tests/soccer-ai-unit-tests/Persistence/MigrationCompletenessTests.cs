using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Application.Entities;

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
            [nameof(db.ModelForecasts)] = () => db.ModelForecasts.CountAsync(),
            [nameof(db.GoalRateModelGenerations)] = () => db.GoalRateModelGenerations.CountAsync(),
            [nameof(db.PredictionSnapshots)] = () => db.PredictionSnapshots.CountAsync(),
            [nameof(db.FixtureInjuries)] = () => db.FixtureInjuries.CountAsync()
        };

        foreach (var (name, count) in counts)
        {
            var act = async () => await count();
            await act.Should().NotThrowAsync($"{name} must have a table created by a migration");
        }

        // Querying a count cannot detect missing columns. Round-trip AI provenance
        // through the migrated schema and an unrelated cache timestamp update.
        var generated = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var analysis = new FixtureAnalysis { FixtureId = 101, AiGeneratedAtUtc = generated,
            AiModelVersion = "model-v1", AiPromptHash = "prompt", AiInputHash = "input" };
        db.FixtureAnalyses.Add(analysis); await db.SaveChangesAsync();
        analysis.UpdatedAt = generated.AddHours(3); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.FixtureAnalyses.SingleAsync();
        stored.AiGeneratedAtUtc.Should().Be(generated);
        stored.AiModelVersion.Should().Be("model-v1");
        stored.AiPromptHash.Should().Be("prompt"); stored.AiInputHash.Should().Be("input");
        db.GoalRateModelGenerations.Add(new GoalRateModelGeneration { Generation = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = generated, ManifestJson = "{}", CalibrationJson = "{}", EvaluationJson = "{}", HomeModel = [1, 2], AwayModel = [3, 4] });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var bundle = await db.GoalRateModelGenerations.OrderByDescending(m => m.CreatedAtUtc).FirstAsync();
        bundle.CreatedAtUtc.Should().Be(generated); bundle.HomeModel.Should().Equal(1, 2); bundle.AwayModel.Should().Equal(3, 4);
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

        db.Model.GetEntityTypes().Should().HaveCount(14,
            "every entity must also be asserted in EveryEntityHasATableAfterMigrating");
    }

    [Fact]
    public void PostgresMigrationScriptIncludesPredictionLedgerAndOddsProvenance()
    {
        // SQL generation does not connect to a database or apply any changes.
        using var db = new PostgresDbContext(new DbContextOptionsBuilder<PostgresDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options);
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        script.Should().Contain("CREATE TABLE \"PredictionSnapshots\"");
        script.Should().Contain("CREATE TABLE \"GoalRateModelGenerations\"");
        script.Should().Contain("\"HomeModel\" bytea");
        script.Should().Contain("\"OddsUpdatedAtUtc\"");
        script.Should().Contain("\"OddsCheckedAtUtc\"");
        script.Should().Contain("\"FixtureInjuries\"");
        foreach (var column in new[] { "AiGeneratedAtUtc", "AiModelVersion", "AiPromptHash", "AiInputHash" })
            script.Should().Contain($"ADD \"{column}\"");
    }
}
