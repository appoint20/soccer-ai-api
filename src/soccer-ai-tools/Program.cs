using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoccerAi.Application;
using SoccerAi.Application.Features.Automation;
using SoccerAi.Application.Features.Backtesting;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure;

namespace SoccerAi.Tools;

/// <summary>
/// CLI entry point for operational commands that were previously hidden behind
/// startup flags in the web API's Program.cs (--backtest, --ml, --sync-*).
/// Usage: soccer-ai-tools &lt;command&gt; [options]
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        using var host = BuildHost(args);

        var command = args[0].ToLowerInvariant();
        try
        {
            // Same behavior as API startup: make sure the schema is current.
            // (migrate-data manages its own contexts; the SQLite source stays read-only there.)
            if (command != "migrate-data")
            {
                using var migrationScope = host.Services.CreateScope();
                var db = migrationScope.ServiceProvider
                    .GetRequiredService<SoccerAi.Infrastructure.Persistence.ApplicationDbContext>();
                await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions
                    .MigrateAsync(db.Database);
            }

            switch (command)
            {
                case "backtest":
                    await RunBacktestAsync(host.Services, args);
                    return 0;
                case "train-ml":
                    await RunMlTrainingAsync(host.Services, args);
                    return 0;
                case "sync-league":
                    return await RunLeagueSyncAsync(host.Services, args);
                case "sync-ai":
                    await RunAiSyncAsync(host.Services, args);
                    return 0;
                case "sync-full":
                    await RunFullSyncAsync(host.Services, args);
                    return 0;
                case "migrate-data":
                    return await RunDataMigrationAsync(host.Services, args);
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Command '{command}' failed: {ex.Message}");
            return 1;
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Load the appsettings.json shipped next to the binary even when the
        // process is started from a different working directory (dotnet run).
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
        builder.Configuration.AddEnvironmentVariables();

        ResolveSqlitePath(builder.Configuration, args);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        var mediatorBuilder = new MediatorBuilder();
        mediatorBuilder.RegisterHandlers(
            typeof(SoccerAi.Application.Features.Analysis.GetMatchAnalysisHandler).Assembly);
        builder.Services.RegisterMediator(mediatorBuilder);

        return builder.Build();
    }

    /// <summary>
    /// Relative SQLite paths depend on the caller's working directory; when run
    /// from the repo root the configured "data/soccer.db" would silently create
    /// an EMPTY database. Resolve --db, or search upward for the existing file
    /// (including the API project's data folder), and pin the absolute path.
    /// </summary>
    private static void ResolveSqlitePath(
        Microsoft.Extensions.Configuration.IConfigurationManager configuration, string[] args)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            return;

        var dbOverride = GetStringOption(args, "--db");
        if (dbOverride != null)
        {
            configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.GetFullPath(dbOverride)}";
            Console.WriteLine($"Using SQLite database: {Path.GetFullPath(dbOverride)}");
            return;
        }

        var configured = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/soccer.db";
        var pathPart = configured.Split('=', 2).ElementAtOrDefault(1)?.Split(';')[0] ?? "data/soccer.db";
        if (Path.IsPathRooted(pathPart))
            return;

        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5 && dir != null; i++)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir, pathPart),
                         Path.Combine(dir, "src", "soccer-ai-api", pathPart),
                         Path.Combine(dir, "soccer-ai-api", pathPart)
                     })
            {
                if (File.Exists(candidate))
                {
                    var full = Path.GetFullPath(candidate);
                    configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={full}";
                    Console.WriteLine($"Using SQLite database: {full}");
                    return;
                }
            }
            dir = Path.GetDirectoryName(dir);
        }

        Console.WriteLine($"WARNING: SQLite file '{pathPart}' not found near '{Directory.GetCurrentDirectory()}' — " +
                          "a new empty database will be created. Pass --db=<path> to use an existing one.");
    }

    private static async Task RunBacktestAsync(IServiceProvider services, string[] args)
    {
        var weeks = GetIntOption(args, "--weeks") ?? 10;
        var stake = GetDoubleOption(args, "--stake") ?? 1.0;
        var refresh = args.Contains("--refresh");

        Console.WriteLine($"Starting backtest pipeline ({weeks} weeks, stake {stake}, refresh: {refresh})...");
        using var scope = services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var response = await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(
            new GetBacktestReportQuery(weeks, stake, refresh));

        var json = System.Text.Json.JsonSerializer.Serialize(
            response, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var outputPath = GetStringOption(args, "--output") ?? "backtest_result.json";
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Backtest complete. JSON written to {outputPath}");
    }

    private static async Task RunMlTrainingAsync(IServiceProvider services, string[] args)
    {
        DateTimeOffset? cutoff = null;
        var cutoffArg = GetStringOption(args, "--cutoff");
        if (cutoffArg != null)
        {
            if (!DateTimeOffset.TryParse(cutoffArg, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                throw new ArgumentException($"Invalid --cutoff value: {cutoffArg} (expected e.g. 2026-03-01)");
            cutoff = parsed;
        }

        Console.WriteLine(cutoff.HasValue
            ? $"Starting ML.NET training pipeline (temporal cutoff {cutoff:yyyy-MM-dd})..."
            : "Starting ML.NET training pipeline (default temporal cutoff)...");

        using var scope = services.CreateScope();
        var mlService = scope.ServiceProvider.GetRequiredService<IMlTrainingService>();
        await mlService.TrainModelsAsync(cutoff);
        Console.WriteLine("ML training complete.");
    }

    private static async Task<int> RunLeagueSyncAsync(IServiceProvider services, string[] args)
    {
        var leagueId = GetIntOption(args, "--league");
        if (leagueId is null)
        {
            Console.Error.WriteLine("sync-league requires --league=<id>");
            return 1;
        }

        var season = GetIntOption(args, "--season") ?? CurrentSeason();
        Console.WriteLine($"Targeted fixture sync for league {leagueId}, season {season}...");

        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IFixtureSyncService>();
        var teamService = scope.ServiceProvider.GetRequiredService<ITeamSyncService>();

        await teamService.SyncLeagueStandingsAsync(leagueId.Value, season, default);
        var result = await syncService.SyncLeagueFixturesAsync(leagueId.Value, season, default);

        Console.WriteLine($"Sync complete: created {result.Created}, updated {result.Updated}");
        return 0;
    }

    private static async Task RunAiSyncAsync(IServiceProvider services, string[] args)
    {
        var fixtureId = GetIntOption(args, "--fixture-id");
        var force = args.Contains("--force");

        Console.WriteLine(fixtureId.HasValue
            ? $"Starting AI analysis sync for fixture {fixtureId} (force: {force})..."
            : $"Starting AI analysis batch sync (force: {force})...");

        using var scope = services.CreateScope();
        var aiSyncService = scope.ServiceProvider.GetRequiredService<IAiSyncService>();

        if (fixtureId.HasValue)
            await aiSyncService.SyncSingleFixtureAsync(fixtureId.Value, force);
        else
            await aiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, force);

        Console.WriteLine("AI sync complete.");
    }

    private static async Task RunFullSyncAsync(IServiceProvider services, string[] args)
    {
        var season = GetIntOption(args, "--season") ?? CurrentSeason();
        Console.WriteLine($"Running full daily sync orchestration for season {season}...");

        using var scope = services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.SendAsync(new RunDailySyncCommand(season));

        Console.WriteLine("Full daily sync orchestration completed.");
    }

    private static async Task<int> RunDataMigrationAsync(IServiceProvider services, string[] args)
    {
        var sqlitePath = GetStringOption(args, "--sqlite") ?? Path.Combine("data", "soccer.db");

        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var postgresConn = GetStringOption(args, "--postgres")
            ?? Microsoft.Extensions.Configuration.ConfigurationExtensions
                .GetConnectionString(configuration, "PostgresConnection");

        if (string.IsNullOrWhiteSpace(postgresConn))
        {
            Console.Error.WriteLine(
                "migrate-data requires --postgres=<connection string> or ConnectionStrings:PostgresConnection in config");
            return 1;
        }

        return await DataMigrationCommand.RunAsync(sqlitePath, postgresConn);
    }

    private static int CurrentSeason() =>
        DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;

    private static string? GetStringOption(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

    private static int? GetIntOption(string[] args, string name) =>
        int.TryParse(GetStringOption(args, name), out var value) ? value : null;

    private static double? GetDoubleOption(string[] args, string name) =>
        double.TryParse(GetStringOption(args, name),
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static void PrintUsage()
    {
        Console.WriteLine("""
            soccer-ai-tools — operational CLI for soccer-ai-api

            Commands:
              backtest     [--weeks=10] [--stake=1.0] [--output=backtest_result.json]
                           Run the backtest pipeline and write the JSON report.
              train-ml     [--cutoff=yyyy-MM-dd]
                           Train the ML.NET models with a temporal train/test split.
                           Rows before the cutoff train; rows on/after it are held out.
              sync-league  --league=<id> [--season=<year>]
                           Sync standings + fixtures for one league.
              sync-ai      [--fixture-id=<id>] [--force]
                           Generate AI analysis for upcoming (or one) fixture.
              sync-full    [--season=<year>]
                           Run the full daily sync orchestration.
              migrate-data [--sqlite=data/soccer.db] [--postgres=<conn string>]
                           One-time zero-loss SQLite → PostgreSQL migration with
                           row-count + checksum verification (aborts on mismatch;
                           the SQLite file is opened read-only).

            Configuration: appsettings.json next to the executable; override via
            environment variables (e.g. CONNECTIONSTRINGS__DEFAULTCONNECTION).
            """);
    }
}
