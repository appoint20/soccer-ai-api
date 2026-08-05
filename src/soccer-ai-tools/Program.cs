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
                case "odds-coverage":
                    return await OddsCoverageCommand.RunAsync(host.Services);
                case "backfill-analysis":
                    return await BackfillAnalysisCommand.RunAsync(host.Services, args);
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

        PrintBacktestSummary(response);
    }

    private static void PrintBacktestSummary(GetBacktestReportResponse r)
    {
        Console.WriteLine();
        Console.WriteLine("=== HEADLINE: QUALIFIED PICKS (valid odds, EV-gated) ===");
        var q = r.QualifiedPicks;
        Console.WriteLine($"  Picks: {q.Count}  Hits: {q.Hits}  Hit rate: {q.HitRate:F1}%  " +
                          $"Avg odds: {q.AvgOdds:F2}  Avg EV: {q.AvgEv:P1}");
        Console.WriteLine($"  Flat ROI: {q.RoiPercent:F1}%   Quarter-Kelly ROI: {q.KellyRoiPercent:F1}%");
        foreach (var m in q.PerMarket)
            Console.WriteLine($"    {m.Market,-14} n={m.Count,-4} hit={m.HitRate,5:F1}%  " +
                              $"odds={m.AvgOdds:F2}  ev={m.AvgEv,6:P1}  flat={m.RoiPercent,6:F1}%  " +
                              $"kelly={m.KellyRoiPercent,6:F1}%");

        Console.WriteLine();
        Console.WriteLine("=== QUALIFICATION FUNNEL (why fixtures dropped out) ===");
        Console.WriteLine($"  {"market",-14} {"total",5} {"noOdds",6} {"minOdds",7} {"minEV",5} {"floor",5} {"veto",5} {"conf",5} {"QUAL",5}");
        foreach (var f in r.QualificationFunnel.Where(f => f.League == "ALL"))
            Console.WriteLine($"  {f.Market,-14} {f.Total,5} {f.AnalysisOnlyNoOdds,6} {f.BelowMinOdds,7} " +
                              $"{f.BelowMinEdge,5} {f.BelowProbabilityFloor,5} {f.Vetoed,5} {f.InsufficientConfirms,5} {f.Qualified,5}");
        Console.WriteLine("  -- per league (aggregated over markets) --");
        foreach (var f in r.QualificationFunnel.Where(f => f.Market == "all"))
            Console.WriteLine($"  {f.League,-22} {f.Total,5} {f.AnalysisOnlyNoOdds,6} {f.BelowMinOdds,7} " +
                              $"{f.BelowMinEdge,5} {f.BelowProbabilityFloor,5} {f.Vetoed,5} {f.InsufficientConfirms,5} {f.Qualified,5}");

        Console.WriteLine();
        Console.WriteLine("=== LEAGUE DIVERGENCE (avg |model − market|, where edge lives) ===");
        foreach (var d in r.LeagueDivergence)
            Console.WriteLine($"  {d.League,-22} n={d.SampleSize,-5} avg={d.AvgDivergence:P1}  " +
                              $"o25={d.Over25:P1}  btts={d.Btts:P1}  1x2={d.MatchWinner:P1}");

        Console.WriteLine();
        Console.WriteLine("=== MARKET QUALITY (all analyzed fixtures) ===");
        foreach (var m in r.MarketMetrics)
            Console.WriteLine($"  {m.Market,-14} n={m.SampleSize,-5} brier={m.BrierScore:F4}  " +
                              $"logloss={m.LogLoss:F4}  valid odds={m.ValidOddsPct:F0}%");

        Console.WriteLine();
        Console.WriteLine("=== CALIBRATION (raw → isotonic-calibrated, vs actual) ===");
        foreach (var market in r.Calibration)
        {
            Console.WriteLine($"  {market.Market}:");
            Console.WriteLine($"    {"range",-10} {"n",-5} {"raw pred",-9} {"raw act",-9} | {"cal n",-5} {"cal pred",-9} {"cal act",-9}");
            var rawByRange = market.RawBuckets.ToDictionary(b => b.Range);
            foreach (var b in market.Buckets)
            {
                var raw = rawByRange.GetValueOrDefault(b.Range);
                if (b.SampleSize == 0 && (raw?.SampleSize ?? 0) == 0) continue;
                Console.WriteLine($"    {b.Range,-10} {raw?.SampleSize ?? 0,-5} {raw?.PredictedAvg ?? 0,-9:P1} {raw?.ActualHitRate ?? 0,-9:P1} | " +
                                  $"{b.SampleSize,-5} {b.PredictedAvg,-9:P1} {b.ActualHitRate,-9:P1}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== ODDS COVERAGE BY WEEK (ROI restricted to ≥ threshold weeks) ===");
        foreach (var wk in r.OddsCoverageWeekly)
            Console.WriteLine($"  {wk.WeekStart:yyyy-MM-dd}  n={wk.Fixtures,-4} withOdds={wk.WithOdds,-4} " +
                              $"{wk.CoveragePct,5:F1}%  {(wk.RoiEligible ? "ROI ✓" : "excluded")}");
        Console.WriteLine($"  (qualified picks excluded from ROI by coverage: {r.QualifiedPicks.ExcludedFromRoi})");

        Console.WriteLine();
        Console.WriteLine("=== SHADOW COHORTS (what the price gates rejected — would-be results) ===");
        foreach (var s in r.ShadowCohorts.Where(s => s.League == "ALL"))
            Console.WriteLine($"  {s.Cohort,-26} {s.Market,-14} n={s.Count,-5} hit={s.HitRate,5:F1}%  " +
                              $"odds={s.AvgOdds:F2}  ev={s.AvgEv,6:P1}  would-be roi={s.WouldBeRoiPercent,6:F1}%");

        Console.WriteLine();
        Console.WriteLine("=== RULE PERFORMANCE (qualified picks, with vs without) ===");
        foreach (var rule in r.RulePerformance)
            Console.WriteLine($"  {rule.Market,-14} {rule.RuleId,-38} " +
                              $"with: n={rule.PicksWith,-4}{rule.HitRateWith,5:F1}%   " +
                              $"without: n={rule.PicksWithout,-4}{rule.HitRateWithout,5:F1}%");

        Console.WriteLine();
        Console.WriteLine("=== TICKETS (ticket-level floors + Kelly) ===");
        var t = r.Tickets.Overall;
        Console.WriteLine($"  overall: n={t.Count} won={t.Won} hit={t.HitRate:F1}%  odds={t.AvgOdds:F2}  " +
                          $"ev={t.AvgEv:P1}  flat={t.FlatRoiPercent:F1}%  kelly={t.KellyRoiPercent:F1}%");
        Console.WriteLine("    (same_match_goals = BTTS+Over2.5 from ONE match, priced at the product;");
        Console.WriteLine("     bookmakers price same-game doubles lower — check the real Bet365 price)");
        foreach (var k in r.Tickets.PerKind.Where(k => k.Count > 0))
            Console.WriteLine($"    {k.Kind,-8} n={k.Count,-4} won={k.Won,-4} hit={k.HitRate,5:F1}%  " +
                              $"odds={k.AvgOdds:F2}  ev={k.AvgEv,6:P1}  flat={k.FlatRoiPercent,6:F1}%  kelly={k.KellyRoiPercent,6:F1}%");

        Console.WriteLine();
        Console.WriteLine($"=== LEGACY COMBOS ===  total={r.Summary.CombosTotal} won={r.Summary.CombosWon} " +
                          $"roi={r.Summary.TotalRoi:F1}%  legs={r.Summary.CorrectLegs}/{r.Summary.TotalLegs}");
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
              odds-coverage
                           Coverage + cause diagnosis per league/season (never
                           fetched vs market missing vs corrupted legacy).
              backfill-analysis [--from=yyyy-MM-dd] [--to=yyyy-MM-dd] [--chunk-days=7]
                           Recompute analysis for historical finished fixtures so
                           the calibration layer gets training data. 0 API calls.
              migrate-data [--sqlite=data/soccer.db] [--postgres=<conn string>]
                           One-time zero-loss SQLite → PostgreSQL migration with
                           row-count + checksum verification (aborts on mismatch;
                           the SQLite file is opened read-only).

            Configuration: appsettings.json next to the executable; override via
            environment variables (e.g. CONNECTIONSTRINGS__DEFAULTCONNECTION).
            """);
    }
}
