using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoccerAi.Application;
using SoccerAi.Application.Features.Automation;
using SoccerAi.Application.Features.Backtesting;
using SoccerAi.Application.Features.Picks;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;
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
                case "capture-injuries":
                    return await RunInjuryCaptureAsync(host.Services);
                case "backfill-odds-provenance":
                    return await RunOddsProvenanceBackfillAsync(host.Services, args);
                case "train-goal-rate":
                    await RunGoalRateTrainingAsync(host.Services);
                    return 0;
                case "forecast":
                    return await RunForecastAsync(host.Services, args);
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
                case "backfill-odds":
                    return await BackfillOddsCommand.RunAsync(host.Services, args);
                case "import-odds-csv":
                    return await ImportOddsCsvCommand.RunAsync(host.Services, args);
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

        var dbOverride = CommandArgs.String(args, "--db");
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
        var weeks = CommandArgs.Int(args, "--weeks") ?? 10;
        var stake = CommandArgs.Double(args, "--stake") ?? 1.0;
        var refresh = CommandArgs.Flag(args, "--refresh");

        Console.WriteLine($"Starting backtest pipeline ({weeks} weeks, stake {stake}, refresh: {refresh})...");
        using var scope = services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var response = await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(
            new GetBacktestReportQuery(weeks, stake, refresh));

        var json = System.Text.Json.JsonSerializer.Serialize(
            response, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var outputPath = CommandArgs.String(args, "--output") ?? "backtest_result.json";
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
        Console.WriteLine("=== EV SWEEP (what each MinEdge level would have produced) ===");
        Console.WriteLine($"  {"minEdge",8} {"picks",6} {"hit%",6} {"odds",6} {"ROI%",7} {"roi n",6}");
        foreach (var e in r.EvSweep)
            Console.WriteLine($"  {e.MinEdge,8:P0} {e.Picks,6} {e.HitRate,6:F1} {e.AvgOdds,6:F2} " +
                              $"{e.RoiPercent,7:F1} {e.RoiSampleSize,6}");

        Console.WriteLine();
        Console.WriteLine("=== CONFIDENCE PICKS — Product 2 (predictions, NOT value bets) ===");
        foreach (var c in r.ConfidencePicks)
            Console.WriteLine($"  {c.Market,-14} n={c.Count,-5} hit={c.HitRate,5:F1}%  " +
                              $"avg p={c.AvgProbability:P1}  {c.PicksPerDay:F1}/day");

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
        var cutoffArg = CommandArgs.String(args, "--cutoff");
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

    /// <summary>
    /// Trains the hybrid goal-rate model: two Tweedie regressors whose output
    /// feeds the Dixon-Coles score matrix. Unlike train-ml, the artefacts this
    /// writes are loaded at serving time by IGoalRateForecaster.
    /// </summary>
    private static async Task RunGoalRateTrainingAsync(IServiceProvider services)
    {
        Console.WriteLine("Training hybrid goal-rate model (ML lambda -> Dixon-Coles)...");

        using var scope = services.CreateScope();
        var trainer = scope.ServiceProvider.GetRequiredService<IGoalRateTrainingService>();
        await trainer.TrainAsync();

        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "models",
            "goal_rate_evaluation.json");
        if (File.Exists(reportPath))
        {
            Console.WriteLine();
            Console.WriteLine(await File.ReadAllTextAsync(reportPath));
        }

        Console.WriteLine("Goal-rate training complete.");
    }

    /// <summary>
    /// Scores upcoming fixtures with the trained hybrid model and prints the
    /// board. Exists to verify end to end that the saved models load and serve —
    /// the exact step that was missing before, when training wrote files nothing
    /// ever read.
    /// </summary>
    /// <summary>
    /// Certifies historical odds as pre-match, from the capture that observed them.
    /// </summary>
    /// <remarks>
    /// GoalRateFeatureBuilder drops every market feature unless a fixture carries
    /// OddsCheckedAtUtc/OddsUpdatedAtUtc, because a bulk-imported closing price
    /// cannot be shown to have been obtainable before kickoff — using one as a
    /// feature is the model reading the result. Those columns only exist from the
    /// AddPredictionSnapshots migration onward, so every pre-existing fixture has
    /// them null and trains with no market signal at all.
    ///
    /// FixtureOddsQuotes already records when each price was fetched. This copies
    /// that time onto the fixture, but ONLY where the evidence actually supports
    /// it: the capture must predate kickoff, and the fixture's stored price must
    /// still equal the best price from that capture. A fixture whose odds were
    /// later overwritten by the historical import fails the second test and keeps
    /// null provenance, which is the correct outcome — its price genuinely cannot
    /// be certified.
    ///
    /// Never overwrites provenance the live capture path has already written.
    /// </remarks>
    /// <summary>
    /// Runs one injury-capture pass. The worker does this on its own loop; this
    /// is for verifying coverage without waiting for a scheduled tick.
    /// </summary>
    private static async Task<int> RunInjuryCaptureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IFixtureSyncService>();
        var db = scope.ServiceProvider
            .GetRequiredService<SoccerAi.Infrastructure.Persistence.ApplicationDbContext>();

        var captured = await sync.CaptureUpcomingInjuriesAsync(CancellationToken.None);

        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(db.FixtureInjuries.OrderByDescending(i => i.CapturedAtUtc).Take(8));

        Console.WriteLine($"fixtures fetched this pass : {captured}");
        Console.WriteLine($"absence rows stored total  : {await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.FixtureInjuries)}");
        foreach (var row in rows)
            Console.WriteLine($"   fixture {row.FixtureId}  {row.PlayerName} — {row.Type} ({row.Reason})  pre-match={row.IsPreMatch}");
        return 0;
    }

    private static async Task<int> RunOddsProvenanceBackfillAsync(IServiceProvider services, string[] args)
    {
        var dryRun = CommandArgs.Flag(args, "--dry-run");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SoccerAi.Infrastructure.Persistence.ApplicationDbContext>();

        var captures = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(db.FixtureOddsQuotes
                .GroupBy(q => q.FixtureId)
                .Select(g => new { FixtureId = g.Key, Captured = g.Max(q => q.CapturedAtUtc) }));

        var capturedBy = captures.ToDictionary(x => x.FixtureId, x => x.Captured);
        var fixtureIds = capturedBy.Keys.ToList();

        // Best price per (fixture, market) — the same rule the fixture columns
        // were written with, so equality here means "this price came from that
        // capture" rather than "these two numbers happen to agree".
        var bestPrices = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.FixtureOddsQuotes
                    .Where(q => fixtureIds.Contains(q.FixtureId))
                    .GroupBy(q => new { q.FixtureId, q.Market })
                    .Select(g => new { g.Key.FixtureId, g.Key.Market, Price = g.Max(q => q.Price) })))
            .ToDictionary(x => (x.FixtureId, x.Market), x => x.Price);

        var fixtures = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(db.Fixtures.Where(f => fixtureIds.Contains(f.Id)));

        int certified = 0, alreadySet = 0, afterKickoff = 0, priceMoved = 0;

        foreach (var fixture in fixtures)
        {
            if (fixture.OddsCheckedAtUtc is not null || fixture.OddsUpdatedAtUtc is not null)
            {
                alreadySet++;
                continue;
            }

            var captured = capturedBy[fixture.Id];
            if (captured >= fixture.Date)
            {
                afterKickoff++;
                continue;
            }

            bool Matches(double? stored, string market) =>
                stored is { } price
                && bestPrices.TryGetValue((fixture.Id, market), out var quoted)
                && Math.Abs(price - quoted) < 1e-9;

            var supported =
                Matches(fixture.HomeWinOdds, OddsMarkets.HomeWin) ||
                Matches(fixture.DrawOdds, OddsMarkets.Draw) ||
                Matches(fixture.AwayWinOdds, OddsMarkets.AwayWin) ||
                Matches(fixture.Over25Odds, OddsMarkets.Over25) ||
                Matches(fixture.Under25Odds, OddsMarkets.Under25) ||
                Matches(fixture.BttsYesOdds, OddsMarkets.BttsYes);

            if (!supported)
            {
                priceMoved++;
                continue;
            }

            // One observation: the price was read and written at the same instant,
            // which is what the freshness guard expects (updated <= checked).
            fixture.OddsCheckedAtUtc = captured;
            fixture.OddsUpdatedAtUtc = captured;
            certified++;
        }

        Console.WriteLine($"fixtures with captured quotes : {fixtures.Count}");
        Console.WriteLine($"  certified pre-match         : {certified}");
        Console.WriteLine($"  skipped, provenance present : {alreadySet}");
        Console.WriteLine($"  skipped, capture >= kickoff : {afterKickoff}");
        Console.WriteLine($"  skipped, price not from it  : {priceMoved}");

        if (dryRun)
        {
            Console.WriteLine("\n--dry-run: nothing written.");
            return 0;
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"\nWrote provenance for {certified} fixture(s).");
        return 0;
    }

    private static async Task<int> RunForecastAsync(IServiceProvider services, string[] args)
    {
        var days = CommandArgs.Int(args, "--days") ?? 1;
        var from = CommandArgs.Date(args, "--from") ?? DateTimeOffset.UtcNow;

        using var scope = services.CreateScope();
        var forecaster = scope.ServiceProvider.GetRequiredService<IGoalRateForecaster>();
        var db = scope.ServiceProvider
            .GetRequiredService<SoccerAi.Infrastructure.Persistence.ApplicationDbContext>();

        var to = from.AddDays(days);
        var upcoming = await db.Fixtures.AsNoTracking()
            .Where(f => f.Status != "FT" && f.Date >= from && f.Date <= to)
            .OrderBy(f => f.Date)
            .ToListAsync();

        if (upcoming.Count == 0)
        {
            Console.WriteLine($"No unplayed fixtures between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");
            return 0;
        }

        var teamIds = upcoming.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.ShortName ?? t.Name);

        Console.WriteLine($"Forecasting {upcoming.Count} fixture(s) from {from:yyyy-MM-dd:yyyy-MM-dd HH:mm} to {to:yyyy-MM-dd:yyyy-MM-dd HH:mm}...");
        var forecasts = await forecaster.ForecastManyAsync(upcoming);

        if (forecasts.Count == 0)
        {
            Console.Error.WriteLine(
                "No forecasts produced. Either no model is trained (run train-goal-rate) "
                + "or HybridModel:Enabled is false.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== TODAY'S MATCHES & MODEL PREDICTIONS ===");
        Console.WriteLine($"{"Kickoff",-16} {"League",-16} {"Match",-36} {"λ_H",-5} {"λ_A",-5} {"1",-6} {"X",-6} {"2",-6} {"O2.5",-6} {"BTTS",-6} {"Top Prediction",-22} {"Value / EV",-15}");
        Console.WriteLine(new string('-', 150));

        var predictionList = new List<object>();

        foreach (var f in upcoming)
        {
            if (!forecasts.TryGetValue(f.Id, out var fc)) continue;
            var p = fc.Probabilities;
            var homeName = teams.GetValueOrDefault(f.HomeTeamId) ?? $"Team {f.HomeTeamId}";
            var awayName = teams.GetValueOrDefault(f.AwayTeamId) ?? $"Team {f.AwayTeamId}";
            var matchStr = $"{homeName} vs {awayName}";
            if (matchStr.Length > 35) matchStr = matchStr[..35];
            var leagueName = LeagueCatalog.Name(f.LeagueId);
            if (leagueName.Length > 15) leagueName = leagueName[..15];

            // The single call, ranked purely by probability across markets —
            // the same rule AnalysisResponseMapper.BuildHeadline applies, so the
            // CLI and the API cannot disagree about what the model put forward.
            //
            // The goals markets used to need p > 0.58 to be eligible while the
            // 1X2 sides needed nothing, which is why a 40% home win outranked a
            // 58% BTTS: the floor applied to one side of the comparison only.
            // Each market is now shown as the side the model actually leans to,
            // so a 30% "over" is reported as a 70% "under" rather than losing.
            var candidates = new (string Label, double Probability)[]
            {
                (p.HomeWin >= p.AwayWin ? $"1 ({homeName})" : $"2 ({awayName})",
                    Math.Max(p.HomeWin, p.AwayWin)),
                ("Draw", p.Draw),
                (p.Over25 >= 0.5 ? "Over 2.5" : "Under 2.5",
                    Math.Max(p.Over25, 1 - p.Over25)),
                (p.BothTeamScoredGoal >= 0.5 ? "BTTS Yes" : "BTTS No",
                    Math.Max(p.BothTeamScoredGoal, 1 - p.BothTeamScoredGoal)),
            };

            var top = candidates.OrderByDescending(c => c.Probability).First();
            var topPred = top.Label;
            var topProb = top.Probability;

            // Check value bets / positive EV
            var valueBets = new List<string>();
            if (f.HomeWinOdds is { } ho && ho > 1.05 && p.HomeWin * ho - 1 > 0.05)
                valueBets.Add($"Home @{ho:F2} (+{(p.HomeWin * ho - 1):P0})");
            if (f.AwayWinOdds is { } ao && ao > 1.05 && p.AwayWin * ao - 1 > 0.05)
                valueBets.Add($"Away @{ao:F2} (+{(p.AwayWin * ao - 1):P0})");
            if (f.DrawOdds is { } dro && dro > 1.05 && p.Draw * dro - 1 > 0.05)
                valueBets.Add($"Draw @{dro:F2} (+{(p.Draw * dro - 1):P0})");
            if (f.Over25Odds is { } oo && oo > 1.05 && p.Over25 * oo - 1 > 0.05)
                valueBets.Add($"O2.5 @{oo:F2} (+{(p.Over25 * oo - 1):P0})");
            if (f.BttsYesOdds is { } bo && bo > 1.05 && p.BothTeamScoredGoal * bo - 1 > 0.05)
                valueBets.Add($"BTTS @{bo:F2} (+{(p.BothTeamScoredGoal * bo - 1):P0})");

            var valueStr = valueBets.Count > 0 ? string.Join(", ", valueBets) : "-";

            Console.WriteLine(
                $"{f.Date:yyyy-MM-dd HH:mm}  {leagueName,-16} {matchStr,-36} {fc.LambdaHome,4:F2} {fc.LambdaAway,4:F2} " +
                $"{p.HomeWin,5:P0} {p.Draw,5:P0} {p.AwayWin,5:P0} {p.Over25,5:P0} {p.BothTeamScoredGoal,5:P0} " +
                $"{topPred + " (" + topProb.ToString("P0") + ")",-22} {valueStr}");

            predictionList.Add(new
            {
                fixture_id = f.Id,
                kickoff = f.Date,
                league = LeagueCatalog.Name(f.LeagueId),
                home_team = homeName,
                away_team = awayName,
                lambda_home = Math.Round(fc.LambdaHome, 2),
                lambda_away = Math.Round(fc.LambdaAway, 2),
                probabilities = new
                {
                    home_win = Math.Round(p.HomeWin, 3),
                    draw = Math.Round(p.Draw, 3),
                    away_win = Math.Round(p.AwayWin, 3),
                    over_25 = Math.Round(p.Over25, 3),
                    btts = Math.Round(p.BothTeamScoredGoal, 3)
                },
                odds = new
                {
                    home = f.HomeWinOdds,
                    draw = f.DrawOdds,
                    away = f.AwayWinOdds,
                    over_25 = f.Over25Odds,
                    btts_yes = f.BttsYesOdds
                },
                top_prediction = topPred,
                top_probability = Math.Round(topProb, 3),
                value_edges = valueBets
            });
        }

        var outputPath = CommandArgs.String(args, "--output") ?? "todays_predictions.json";
        await File.WriteAllTextAsync(outputPath,
            System.Text.Json.JsonSerializer.Serialize(predictionList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine($"Saved full match predictions JSON ({predictionList.Count} fixtures) to {outputPath}");

        // Now run DailyPickService to get today's official product board (tickets + confidence picks)
        try
        {
            var pickService = scope.ServiceProvider.GetRequiredService<IDailyPickService>();
            var today = DateOnly.FromDateTime(from.UtcDateTime);
            var board = await pickService.GetBoardAsync(today, "en");
            var resp = DailyPickBoardMapper.ToResponse(board);

            Console.WriteLine();
            Console.WriteLine($"=== DAILY PICKS PRODUCT BOARD FOR {today:yyyy-MM-dd} ===");
            Console.WriteLine($"Coverage: {resp.Coverage.Analyzed}/{resp.Coverage.Fixtures} fixtures analyzed, {resp.Coverage.Priced} with live odds ({resp.Coverage.PricedPct}%).");

            Console.WriteLine();
            Console.WriteLine("--- TOP CONFIDENCE PICKS ---");
            if (resp.ConfidencePicks.Count == 0)
            {
                Console.WriteLine("  No confidence picks passed the minimum probability threshold today.");
            }
            else
            {
                foreach (var cp in resp.ConfidencePicks)
                {
                    Console.WriteLine($"  {cp.Match,-35} {cp.Market,-14} {cp.Selection,-16} Model P: {cp.ModelProbability:P1} (Kickoff: {cp.KickoffUtc:HH:mm} UTC)");
                }
            }

            Console.WriteLine();
            Console.WriteLine("--- OFFICIAL STAKEABLE TICKETS (Singles & Combos) ---");
            var allTickets = resp.Singles.Concat(resp.SameMatchPairs).Concat(resp.Combos).ToList();
            if (allTickets.Count == 0)
            {
                Console.WriteLine("  No tickets cleared the positive EV + Kelly gate today.");
            }
            else
            {
                foreach (var t in allTickets)
                {
                    Console.WriteLine($"  [{t.Kind.ToUpper()}] Total Odds: {t.TotalOdds:F2}  Model P: {t.Probability:P1}  EV: {t.Ev:P1}  Quarter-Kelly: {t.KellyStake:P1}");
                    foreach (var leg in t.Legs)
                    {
                        Console.WriteLine($"     • {leg.Match} | {leg.Market}: {leg.Selection} @ {leg.Odds:F2} (P: {leg.Probability:P1}, EV: {leg.Ev:P1})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Notice: Daily pick board computation note: {ex.Message}");
        }

        return 0;
    }

    private static async Task<int> RunLeagueSyncAsync(IServiceProvider services, string[] args)
    {
        var leagueId = CommandArgs.Int(args, "--league");
        if (leagueId is null)
        {
            Console.Error.WriteLine("sync-league requires --league=<id>");
            return 1;
        }

        var season = CommandArgs.Int(args, "--season") ?? CurrentSeason();
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
        var fixtureId = CommandArgs.Int(args, "--fixture-id");
        var force = CommandArgs.Flag(args, "--force");
        var days = CommandArgs.Int(args, "--days") ?? 5;

        Console.WriteLine(fixtureId.HasValue
            ? $"Starting AI analysis sync for fixture {fixtureId} (force: {force})..."
            : $"Starting AI analysis batch sync for the next {days} day(s) (force: {force})...");

        using var scope = services.CreateScope();
        var aiSyncService = scope.ServiceProvider.GetRequiredService<IAiSyncService>();

        if (fixtureId.HasValue)
            await aiSyncService.SyncSingleFixtureAsync(fixtureId.Value, force);
        else
            await aiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, force, days);

        Console.WriteLine("AI sync complete.");
    }

    private static async Task RunFullSyncAsync(IServiceProvider services, string[] args)
    {
        var season = CommandArgs.Int(args, "--season") ?? CurrentSeason();
        Console.WriteLine($"Running full daily sync orchestration for season {season}...");

        using var scope = services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.SendAsync(new RunDailySyncCommand(season));

        Console.WriteLine("Full daily sync orchestration completed.");
    }

    private static async Task<int> RunDataMigrationAsync(IServiceProvider services, string[] args)
    {
        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        // Without --sqlite, use the file ResolveSqlitePath already located by
        // searching upward. Falling back to the raw relative default instead
        // would report "not found" for a database the process had just printed
        // the absolute path of, one line earlier.
        var sqlitePath = CommandArgs.String(args, "--sqlite") ?? ResolvedSqlitePath(configuration);

        var postgresConn = CommandArgs.String(args, "--postgres")
            ?? Microsoft.Extensions.Configuration.ConfigurationExtensions
                .GetConnectionString(configuration, "PostgresConnection");

        if (string.IsNullOrWhiteSpace(postgresConn))
        {
            Console.Error.WriteLine(
                "migrate-data requires --postgres=<connection string> or ConnectionStrings:PostgresConnection in config");
            return 1;
        }

        Console.WriteLine($"Source (SQLite) : {Path.GetFullPath(sqlitePath)}");
        Console.WriteLine($"Target (Postgres): {ConnectionStringRedactor.Redact(postgresConn)}");
        Console.WriteLine();

        return await DataMigrationCommand.RunAsync(sqlitePath, postgresConn);
    }

    private static string ResolvedSqlitePath(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var configured = Microsoft.Extensions.Configuration.ConfigurationExtensions
            .GetConnectionString(configuration, "DefaultConnection");

        var pathPart = configured?.Split('=', 2).ElementAtOrDefault(1)?.Split(';')[0];

        return string.IsNullOrWhiteSpace(pathPart) ? Path.Combine("data", "soccer.db") : pathPart;
    }

    private static int CurrentSeason() =>
        DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;

    private static void PrintUsage()
    {
        Console.WriteLine("""
            soccer-ai-tools — operational CLI for soccer-ai-api

            Commands:
              backtest     [--weeks=10] [--stake=1.0] [--output=backtest_result.json]
                           Run the backtest pipeline and write the JSON report.
              train-ml     [--cutoff=yyyy-MM-dd]
              capture-injuries
              backfill-odds-provenance [--dry-run]
              train-goal-rate
              forecast     [--days=N] [--from=yyyy-MM-dd]
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
              backfill-odds [--from=yyyy-MM-dd] [--to=yyyy-MM-dd] [--max-calls=N] [--probe]
                           Repair odds for fixtures missed while the worker was
                           down. API-Football keeps only 7 days of pre-match odds,
                           so this recovers a short outage, NOT history. Older odds
                           are gone permanently. Never invents a price.
                           Start with --probe.
              import-odds-csv [--seasons=2021,2022] [--from-season=2020] [--dry-run]
                           One-time import of historical Bet365 1X2 + O/U 2.5
                           prices from football-data.co.uk, which API-Football
                           cannot serve. Never overwrites a live-captured price.
                           Unmatched team names are reported, not guessed.
                           Start with --dry-run.
              migrate-data [--sqlite=data/soccer.db] [--postgres=<conn string>]
                           One-time zero-loss SQLite → PostgreSQL migration with
                           row-count + checksum verification (aborts on mismatch;
                           the SQLite file is opened read-only).

            Configuration: appsettings.json next to the executable; override via
            environment variables (e.g. CONNECTIONSTRINGS__DEFAULTCONNECTION).
            """);
    }
}
