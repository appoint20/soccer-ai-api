using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Infrastructure.Persistence;
using System.Diagnostics;

namespace MigrationTool
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting SQLite to PostgreSQL Data Migration...");

            var sqlitePath = System.IO.Path.GetFullPath("../soccer-ai-api/data/soccer.db");
            var sqliteConnectionString = $"Data Source={sqlitePath}";
            
            // NOTE: Add your password here or run it with an env var
            var pgConnectionString = "Host=34.141.104.185;Database=soccer_ai;Username=soccer_app;Password=Fx1h?*6[aSNtRDl4;";

            var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(sqliteConnectionString)
                .Options;

            var pgOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(pgConnectionString)
                .Options;

            using var source = new ApplicationDbContext(sqliteOptions);
            using var dest = new ApplicationDbContext(pgOptions);

            Console.WriteLine("Ensuring target database is created and migrations are applied...");
            dest.Database.Migrate(); // Ensures Postgres has the correct schema!

            ClearTargetDatabase(dest);
            MigrateData(source, dest);

            Console.WriteLine("Migration completed successfully.");
        }

        static void ClearTargetDatabase(ApplicationDbContext dest)
        {
            Console.WriteLine("Clearing existing data from target database...");
            dest.Users.RemoveRange(dest.Users);
            dest.FixtureAnalyses.RemoveRange(dest.FixtureAnalyses);
            dest.Fixtures.RemoveRange(dest.Fixtures);
            dest.Teams.RemoveRange(dest.Teams);
            dest.SaveChanges();
            Console.WriteLine("Target database cleared.");
        }

        static void MigrateData(ApplicationDbContext source, ApplicationDbContext dest)
        {
            // 1. Users
            var users = source.Users.AsNoTracking().ToList();
            Console.WriteLine($"Migrating {users.Count} Users...");
            dest.Users.AddRange(users);
            dest.SaveChanges();

            // 2. Teams
            var teams = source.Teams.AsNoTracking().ToList();
            Console.WriteLine($"Migrating {teams.Count} Teams...");
            dest.Teams.AddRange(teams);
            dest.SaveChanges();

            // 4. Fixtures
            var fixtures = source.Fixtures.AsNoTracking().ToList();
            Console.WriteLine($"Migrating {fixtures.Count} Fixtures...");
            dest.Fixtures.AddRange(fixtures);
            dest.SaveChanges();

            // 5. FixtureAnalyses
            var analyses = source.FixtureAnalyses.AsNoTracking().ToList();
            Console.WriteLine($"Migrating {analyses.Count} FixtureAnalyses...");
            // Need to batch these because they contain large text objects and might exceed EF's limits
            int batchSize = 100;
            for (int i = 0; i < analyses.Count; i += batchSize)
            {
                var batch = analyses.Skip(i).Take(batchSize).ToList();
                dest.FixtureAnalyses.AddRange(batch);
                dest.SaveChanges();
                dest.ChangeTracker.Clear();
                Console.WriteLine($"  Migrated {Math.Min(i + batchSize, analyses.Count)} / {analyses.Count} Analyses...");
            }

        }
    }
}
