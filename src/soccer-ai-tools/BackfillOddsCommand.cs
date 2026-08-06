using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;

namespace SoccerAi.Tools;

/// <summary>
/// Recovers odds the routine sync could never fetch.
///
/// The sync only looks for odds inside a short lookback window, so fixtures
/// that aged past it while the worker was down have no price at all — and a
/// fixture without a price cannot be value-checked, however confident the model
/// is about it. That is the single largest cause of low pick volume.
///
/// Only real quoted prices are written. Nothing is defaulted or estimated.
/// </summary>
public static class BackfillOddsCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        using var scope = services.CreateScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IOddsBackfillService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OddsSyncOptions>>().Value;

        var to = ParseDate(args, "--to") ?? DateTimeOffset.UtcNow;
        var from = ParseDate(args, "--from") ?? to.AddDays(-210);
        var maxCalls = ParseInt(args, "--max-calls") ?? options.BackfillMaxCalls;
        var probeOnly = args.Contains("--probe");

        if (from > to)
        {
            Console.Error.WriteLine("--from must not be after --to.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"=== ODDS BACKFILL  {from:yyyy-MM-dd} → {to:yyyy-MM-dd} ===");
        Console.WriteLine(probeOnly
            ? $"Probe only ({options.BackfillProbeSize} fixtures). No bulk calls will be spent."
            : $"Budget: {maxCalls} API calls. Stops early if the daily quota gets tight.");
        Console.WriteLine();

        if (probeOnly)
        {
            var probe = await backfill.ProbeAsync(from, to, options.BackfillProbeSize);
            PrintProbe(probe, options);
            return 0;
        }

        var result = await backfill.BackfillAsync(from, to, maxCalls);

        Console.WriteLine($"  Unpriced fixtures found : {result.MissingBefore}");
        Console.WriteLine($"  Attempted               : {result.Attempted}");
        Console.WriteLine($"  Newly priced            : {result.Filled}");
        Console.WriteLine($"  API calls used          : {result.CallsUsed}");
        Console.WriteLine($"  Stopped because         : {Explain(result.StopReason)}");
        Console.WriteLine();

        var remaining = result.MissingBefore - result.Filled;
        if (remaining > 0 && result.StopReason == OddsBackfillResult.MaxCallsReached)
        {
            Console.WriteLine($"  {remaining} fixtures still unpriced. Run again to continue —");
            Console.WriteLine("  already-priced fixtures are skipped, so nothing is repeated.");
        }
        else if (result.StopReason == OddsBackfillResult.ProbeTooLow)
        {
            Console.WriteLine("  The API no longer prices this window. Backfilling it is not possible;");
            Console.WriteLine("  keeping the worker running is what prevents future gaps.");
        }

        Console.WriteLine();
        Console.WriteLine("  Run 'odds-coverage' to see the coverage this produced.");
        return 0;
    }

    private static void PrintProbe(OddsBackfillProbe probe, OddsSyncOptions options)
    {
        if (probe.Sampled == 0)
        {
            Console.WriteLine("  No unpriced fixtures in this window — nothing to back-fill.");
            return;
        }

        Console.WriteLine($"  Sampled     : {probe.Sampled}");
        Console.WriteLine($"  Still priced: {probe.Priced}  ({probe.HitRate:P0})");
        Console.WriteLine();
        Console.WriteLine(probe.HitRate >= options.BackfillMinProbeHitRate
            ? "  Worth running the full backfill."
            : "  Not worth it — the API no longer prices this far back.");
    }

    private static string Explain(string reason) => reason switch
    {
        OddsBackfillResult.Completed => "finished the whole window",
        OddsBackfillResult.MaxCallsReached => "hit the API call budget",
        OddsBackfillResult.QuotaCritical => "daily API quota nearly exhausted",
        OddsBackfillResult.ProbeTooLow => "the sample showed the API no longer prices this window",
        OddsBackfillResult.Cancelled => "cancelled",
        _ => reason
    };

    private static DateTimeOffset? ParseDate(string[] args, string name)
    {
        var raw = Value(args, name);
        return raw is not null && DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string[] args, string name) =>
        int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? Value(string[] args, string name) => args
        .FirstOrDefault(a => a.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        ?[(name.Length + 1)..];
}
