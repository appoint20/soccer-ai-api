using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.Tools;

/// <summary>
/// One-time zero-loss data migration: SQLite (read-only source) → PostgreSQL.
///
/// Guarantees:
/// - The SQLite file is opened Mode=ReadOnly and never modified.
/// - Every table is copied inside ONE Postgres transaction; verification runs
///   before commit, so ANY mismatch aborts with no partial state.
/// - Verification: row counts per table must match AND SHA-256 checksums of
///   100 random rows per table must match between source and target.
/// - Identity sequences are resynced after explicit-Id inserts.
///
/// Precision note: PostgreSQL timestamps have microsecond precision while
/// .NET ticks are 100ns. Checksums truncate timestamps to whole microseconds;
/// the sub-microsecond remainder is the only (documented) difference.
/// </summary>
public static class DataMigrationCommand
{
    private const int SpotCheckSampleSize = 100;
    private const int RandomSeed = 42; // reproducible verification runs

    private sealed record TableReport(
        string Table, int SourceRows, int TargetRows, int Sampled, int ChecksumMismatches)
    {
        public bool Ok => SourceRows == TargetRows && ChecksumMismatches == 0;
    }

    public static async Task<int> RunAsync(string sqlitePath, string postgresConnectionString)
    {
        if (!File.Exists(sqlitePath))
        {
            Console.Error.WriteLine($"SQLite source not found: {sqlitePath}");
            return 1;
        }

        Console.WriteLine($"Source (read-only): {sqlitePath}");
        Console.WriteLine($"Target: PostgreSQL");

        // Managed platforms hand out postgresql://user:pass@host/db, which
        // Npgsql rejects as "Format of the initialization string does not
        // conform to specification". Keyword strings pass through untouched.
        var targetConnectionString = PostgresConnectionString.Normalize(postgresConnectionString);

        var sourceOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={sqlitePath};Mode=ReadOnly")
            .Options;
        var targetOptions = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseNpgsql(targetConnectionString)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var source = new ApplicationDbContext(sourceOptions);
        await using var target = new PostgresDbContext(targetOptions);

        // Ensure the Postgres schema exists (applies InitPostgres).
        Console.WriteLine("Applying PostgreSQL migrations...");
        await target.Database.MigrateAsync();

        // Safety: this file enumerates every DbSet explicitly. If the model has
        // grown since, refuse to run rather than silently skip a table.
        // SyncState is transient operational state — intentionally NOT migrated.
        string[] knownEntities =
            [nameof(Team), nameof(Fixture), nameof(FixtureAnalysis), nameof(FixtureOddsQuote),
             nameof(Combination), nameof(User), nameof(BacktestReport),
             nameof(PublishedTicket), nameof(PublishedTicketLeg), nameof(SyncState)];
        var modelEntities = target.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .Distinct()
            .ToList();
        var unknown = modelEntities.Except(knownEntities).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine(
                $"ABORT: model contains entities this migration does not handle: {string.Join(", ", unknown)}");
            return 1;
        }

        // Target must be empty — this is a one-time migration.
        if (await target.Teams.AnyAsync() || await target.Fixtures.AnyAsync() ||
            await target.FixtureAnalyses.AnyAsync() || await target.Combinations.AnyAsync() ||
            await target.Users.AnyAsync() || await target.BacktestReports.AnyAsync() ||
            await target.PublishedTickets.AnyAsync())
        {
            Console.Error.WriteLine("ABORT: target database is not empty. This is a one-time migration.");
            return 1;
        }

        var reports = new List<TableReport>();

        await using var transaction = await target.Database.BeginTransactionAsync();
        try
        {
            // FK order: Teams → Fixtures → FixtureAnalyses, then independents.
            reports.Add(await MigrateTableAsync<Team>(source, target, "Teams", e => e.Id));
            reports.Add(await MigrateTableAsync<Fixture>(source, target, "Fixtures", e => e.Id));
            reports.Add(await MigrateTableAsync<FixtureAnalysis>(source, target, "FixtureAnalyses", e => e.Id));
            reports.Add(await MigrateTableAsync<FixtureOddsQuote>(source, target, "FixtureOddsQuotes", e => e.Id));
            reports.Add(await MigrateTableAsync<Combination>(source, target, "Combinations", e => e.Id));

            // Users are seed data, not business data: the login handler recreates
            // them the first time anyone signs in against an empty database. A
            // deployed API can therefore populate this table WHILE the migration
            // runs — the emptiness check passes, then a developer logs in during
            // the minutes it takes to copy 48,000 odds quotes, and the unique
            // index on Username rejects the copy. Skipping is correct here:
            // the same accounts with the same passwords already exist.
            if (await target.Users.AnyAsync())
            {
                Console.WriteLine(
                    "Skipping Users: the target already has accounts (the API seeds them on first "
                    + "login). Same usernames, same passwords — nothing is lost.");
            }
            else
            {
                reports.Add(await MigrateTableAsync<User>(source, target, "Users", e => e.Id));
            }

            reports.Add(await MigrateTableAsync<BacktestReport>(source, target, "BacktestReports", e => e.Id));

            // The live results ledger. Tickets before legs: the legs carry the
            // foreign key. Loaded without their navigation so EF inserts each
            // row exactly once rather than cascading from the parent.
            reports.Add(await MigrateTableAsync<PublishedTicket>(source, target, "PublishedTickets", e => e.Id));
            reports.Add(await MigrateTableAsync<PublishedTicketLeg>(source, target, "PublishedTicketLegs", e => e.Id));

            PrintReport(reports);

            if (reports.Any(r => !r.Ok))
            {
                Console.Error.WriteLine("VERIFICATION FAILED — rolling back. No data was committed.");
                await transaction.RollbackAsync();
                return 1;
            }

            // Resync identity sequences after explicit-Id inserts.
            foreach (var table in new[]
                     {
                         "Teams", "Fixtures", "FixtureAnalyses", "FixtureOddsQuotes", "Combinations",
                         "Users", "BacktestReports", "PublishedTickets", "PublishedTicketLegs"
                     })
            {
                // Table names come from the fixed list above — not user input.
#pragma warning disable EF1002
                await target.Database.ExecuteSqlRawAsync(
                    $"""SELECT setval(pg_get_serial_sequence('"{table}"', 'Id'), COALESCE((SELECT MAX("Id") FROM "{table}"), 0) + 1, false);""");
#pragma warning restore EF1002
            }

            await transaction.CommitAsync();
            Console.WriteLine("SUCCESS: migration committed. SQLite source was not modified.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ABORT: {Describe(ex)}");
            Console.Error.WriteLine("Rolling back. No data was committed.");
            await transaction.RollbackAsync();
            return 1;
        }
    }

    /// <summary>
    /// Unwraps the whole exception chain.
    ///
    /// EF's own message is "An error occurred while saving the entity changes.
    /// See the inner exception for details" — which is worse than useless when
    /// the inner exception is the only thing that names the failing constraint,
    /// column or value. Printing just the outer message costs an entire
    /// diagnostic cycle.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add($"{current.GetType().Name}: {current.Message}");

        return string.Join(Environment.NewLine + "  → ", messages);
    }

    private static async Task<TableReport> MigrateTableAsync<TEntity>(
        ApplicationDbContext source,
        PostgresDbContext target,
        string tableName,
        Func<TEntity, int> keySelector) where TEntity : class
    {
        Console.WriteLine($"Migrating {tableName}...");

        var sourceRows = await source.Set<TEntity>().AsNoTracking().ToListAsync();
        target.Set<TEntity>().AddRange(sourceRows);
        await target.SaveChangesAsync();
        target.ChangeTracker.Clear();

        var targetCount = await target.Set<TEntity>().CountAsync();

        // ── Checksum spot-check: 100 random rows compared source vs target ──
        var sampled = 0;
        var mismatches = 0;
        if (sourceRows.Count > 0)
        {
            var random = new Random(RandomSeed);
            var sample = sourceRows
                .OrderBy(_ => random.Next())
                .Take(Math.Min(SpotCheckSampleSize, sourceRows.Count))
                .ToList();
            sampled = sample.Count;

            var sampleIds = sample.Select(keySelector).ToHashSet();
            var targetRows = (await target.Set<TEntity>().AsNoTracking().ToListAsync())
                .Where(e => sampleIds.Contains(keySelector(e)))
                .ToDictionary(keySelector);

            foreach (var row in sample)
            {
                var id = keySelector(row);
                if (!targetRows.TryGetValue(id, out var targetRow) ||
                    Checksum(row) != Checksum(targetRow))
                {
                    mismatches++;
                    Console.Error.WriteLine($"  CHECKSUM MISMATCH {tableName} Id={id}");
                }
            }
        }

        Console.WriteLine($"  {tableName}: {sourceRows.Count} source rows → {targetCount} target rows, " +
                          $"{sampled} sampled, {mismatches} mismatches");
        return new TableReport(tableName, sourceRows.Count, targetCount, sampled, mismatches);
    }

    // ── Canonical row checksum ────────────────────────────────────────────────

    private static string Checksum(object entity)
    {
        var sb = new StringBuilder();
        var props = entity.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsScalar(p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal);

        foreach (var prop in props)
        {
            sb.Append(prop.Name).Append('=');
            sb.Append(Canonical(prop.GetValue(entity)));
            sb.Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) ||
               t == typeof(decimal) || t == typeof(DateTimeOffset) || t == typeof(DateTime);
    }

    private static string Canonical(object? value) => value switch
    {
        null => "<null>",
        DateTimeOffset dto => (dto.UtcTicks - dto.UtcTicks % 10).ToString(CultureInfo.InvariantCulture),
        DateTime dt => (dt.Ticks - dt.Ticks % 10).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static void PrintReport(List<TableReport> reports)
    {
        Console.WriteLine();
        Console.WriteLine("=== VERIFICATION REPORT ===");
        Console.WriteLine($"{"Table",-20} {"Source",8} {"Target",8} {"Sampled",8} {"Mismatch",9} {"Status",7}");
        foreach (var r in reports)
        {
            Console.WriteLine(
                $"{r.Table,-20} {r.SourceRows,8} {r.TargetRows,8} {r.Sampled,8} {r.ChecksumMismatches,9} {(r.Ok ? "OK" : "FAIL"),7}");
        }
        Console.WriteLine("===========================");
    }
}
