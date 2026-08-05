using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Tools;

/// <summary>
/// Recomputes analysis snapshots for historical FINISHED fixtures so the
/// walk-forward isotonic calibration layer has training data.
///
/// Costs ZERO API calls: the Dixon-Coles model, strategic signals and the
/// decision layer all read from the local database. Only the daily sync
/// window (±3/4 days) writes these rows normally, so without a backfill it
/// takes weeks to reach the 300-sample activation threshold.
/// </summary>
public static class BackfillAnalysisCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var from = ParseDate(GetOption(args, "--from")) ?? DateTimeOffset.UtcNow.AddYears(-1);
        var to = ParseDate(GetOption(args, "--to")) ?? DateTimeOffset.UtcNow;
        var chunkDays = int.TryParse(GetOption(args, "--chunk-days"), out var cd) ? cd : 7;

        using var countScope = services.CreateScope();
        var db = countScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var leagueTiers = countScope.ServiceProvider.GetRequiredService<ILeagueTierService>();
        var scoped = leagueTiers.GetSyncLeagueIds().ToList();

        var total = await db.Fixtures
            .CountAsync(f => f.Status == "FT" && f.Date >= from && f.Date < to && scoped.Contains(f.LeagueId));

        Console.WriteLine($"Backfilling analysis for {total} finished fixtures");
        Console.WriteLine($"  window : {from:yyyy-MM-dd} → {to:yyyy-MM-dd}");
        Console.WriteLine($"  chunk  : {chunkDays} days");
        Console.WriteLine("  cost   : 0 API calls (database only)");
        Console.WriteLine();

        if (total == 0)
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        var sw = Stopwatch.StartNew();
        var processed = 0;
        var cursor = from;

        while (cursor < to)
        {
            var chunkEnd = cursor.AddDays(chunkDays);
            if (chunkEnd > to) chunkEnd = to;

            // Fresh scope per chunk: keeps the EF change tracker small.
            using var scope = services.CreateScope();
            var precompute = scope.ServiceProvider.GetRequiredService<IAnalysisPrecomputeService>();

            try
            {
                var done = await precompute.RecomputeWindowAsync(cursor, chunkEnd);
                processed += done;

                var pct = total > 0 ? processed * 100.0 / total : 100;
                Console.WriteLine(
                    $"  {cursor:yyyy-MM-dd} → {chunkEnd:yyyy-MM-dd}: {done,4} fixtures " +
                    $"(total {processed}/{total}, {pct:F0}%, {sw.Elapsed:hh\\:mm\\:ss} elapsed)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  {cursor:yyyy-MM-dd}: FAILED — {ex.Message}");
            }

            cursor = chunkEnd;
        }

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"Backfill complete: {processed} fixtures in {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine("The isotonic calibration layer activates once a market reaches 300 samples.");
        return 0;
    }

    private static string? GetOption(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
