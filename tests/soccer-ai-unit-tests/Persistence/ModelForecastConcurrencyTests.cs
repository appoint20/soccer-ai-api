using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Persistence;

public sealed class ModelForecastConcurrencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "soccer-forecast-race-" + Guid.NewGuid().ToString("N") + ".db");
    // PostgreSQL timestamps preserve microseconds, not .NET's final 100ns digit.
    private readonly DateTimeOffset _kickoff = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero).AddDays(1);
    private readonly string _schema = "soccer_worker_test_" + Guid.NewGuid().ToString("N");
    private string? _postgres;
    private ApplicationDbContext Context(DbCommandInterceptor? interceptor = null)
    {
        if (_postgres != null)
        {
            var pgOptions = new DbContextOptionsBuilder<PostgresDbContext>().UseNpgsql(_postgres);
            if (interceptor != null) pgOptions.AddInterceptors(interceptor);
            return new PostgresDbContext(pgOptions.Options);
        }
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={_path};Pooling=False");
        if (interceptor != null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private async Task Seed()
    {
        await using var db = Context();
        if (_postgres != null)
        {
            // A generated schema in a dedicated LOCAL test database only.
            // Identifiers cannot be bound as SQL values. _schema is generated
            // entirely from a constant prefix plus a GUID, never external input.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{_schema}\"");
#pragma warning restore EF1002
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }
        else await db.Database.EnsureCreatedAsync();
        db.Teams.AddRange(new Team { ApiId = 10 }, new Team { ApiId = 20 });
        db.Fixtures.Add(new Fixture { Id = 1, ApiId = 1, Date = _kickoff, Status = "NS", HomeTeamId = 10, AwayTeamId = 20 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_contexts_that_both_see_no_forecast_keep_one_row_and_can_still_save_status()
        => await ConcurrentInsert();

    [LocalPostgresFact]
    public async Task Postgres_concurrent_inserts_preserve_the_first_forecast_and_leave_contexts_usable()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SOCCER_TEST_POSTGRES"));
        if (connection.Host is not ("localhost" or "127.0.0.1") || connection.Database != "soccer_worker_regression")
            throw new InvalidOperationException("Postgres regression tests require a dedicated local soccer_worker_regression database.");
        connection.SearchPath = _schema; connection.Pooling = false; _postgres = connection.ConnectionString;
        await ConcurrentInsert();
    }

    private async Task ConcurrentInsert()
    {
        await Seed(); var barrier = new BothLookupsCompleted();
        await using var first = Context(barrier); await using var second = Context(barrier);
        var analysis = new MatchAnalysis { Id = 1, Date = _kickoff, Prediction = new PredictionResponse {
            Over25 = new BoolPrediction { Probability = .55 }, BTTS = new BoolPrediction { Probability = .6 } } };
        var forecast = new GoalsForecast { Model = "provider/model's-name", ExpectedGoals = 3, PredictedHomeGoals = 2,
            PredictedAwayGoals = 1, Over25Probability = .7, BttsProbability = .65, Confidence = .8, Rationale = "First pre-match forecast" };
        var a = new ModelForecastLedger(first, NullLogger<ModelForecastLedger>.Instance);
        var b = new ModelForecastLedger(second, NullLogger<ModelForecastLedger>.Instance);
        await Task.WhenAll(a.RecordAsync(analysis, [forecast]), b.RecordAsync(analysis, [forecast with { Over25Probability = .9 }]));
        first.ChangeTracker.Entries<ModelForecast>().Should().NotContain(e => e.State == EntityState.Added);
        second.ChangeTracker.Entries<ModelForecast>().Should().NotContain(e => e.State == EntityState.Added);
        first.SyncStates.Add(new SyncState { Id = 1, LastCompletedStep = "ai_narratives" });
        await first.SaveChangesAsync(); await second.SaveChangesAsync();
        await using var verify = Context();
        var stored = await verify.ModelForecasts.SingleAsync();
        stored.Model.Should().Be(forecast.Model); stored.KickoffUtc.Should().Be(_kickoff);
        new[] { .7, .9 }.Should().Contain(stored.Over25Probability);
        var before = stored.Over25Probability;
        await a.RecordAsync(analysis, [forecast with { Over25Probability = .99 }]);
        (await verify.ModelForecasts.AsNoTracking().SingleAsync()).Over25Probability.Should().Be(before);
        (await verify.SyncStates.SingleAsync()).LastCompletedStep.Should().Be("ai_narratives");
    }

    [Fact]
    public async Task Conflict_handling_does_not_hide_other_constraints_or_accept_changed_kickoff()
    {
        await Seed(); await using var db = Context();
        var row = new ModelForecast { FixtureId = 1, Model = "m", KickoffUtc = _kickoff, PredictedAtUtc = DateTimeOffset.UtcNow };
        (await db.TryInsertModelForecastAsync(row, CancellationToken.None)).Should().BeTrue();
        (await db.TryInsertModelForecastAsync(row, CancellationToken.None)).Should().BeFalse();
        row.Model = "later"; row.KickoffUtc = _kickoff.AddHours(1);
        (await db.TryInsertModelForecastAsync(row, CancellationToken.None)).Should().BeFalse();
        row.KickoffUtc = _kickoff; row.Model = null!;
        var invalid = () => db.TryInsertModelForecastAsync(row, CancellationToken.None);
        (await invalid.Should().ThrowAsync<SqliteException>()).Which.SqliteExtendedErrorCode.Should().Be(1299); // NOT NULL
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private sealed class BothLookupsCompleted : DbCommandInterceptor
    {
        private int _reads;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"ModelForecasts\"", StringComparison.Ordinal) && Interlocked.Increment(ref _reads) <= 2)
            {
                if (Volatile.Read(ref _reads) == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
    public void Dispose()
    {
        if (_postgres != null)
        {
            using var db = Context();
#pragma warning disable EF1002 // Generated test-only identifier, as in Seed.
            db.Database.ExecuteSqlRaw($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE");
#pragma warning restore EF1002
        }
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
    }
}

public sealed class LocalPostgresFactAttribute : FactAttribute
{
    public LocalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SOCCER_TEST_POSTGRES")))
            Skip = "Set SOCCER_TEST_POSTGRES to a dedicated local soccer_worker_regression database.";
    }
}
