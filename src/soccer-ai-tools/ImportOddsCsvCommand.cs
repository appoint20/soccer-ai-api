using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Tools;

/// <summary>
/// One-time import of historical Bet365 prices from football-data.co.uk.
///
/// API-Football keeps only seven days of pre-match odds, so most finished
/// fixtures have no price and are invisible to the value gate — which is why
/// the backtest sees 26 picks instead of hundreds. These free season files carry
/// Bet365 1X2 and Over/Under 2.5 going back years, and close that gap for the
/// backtest. They carry no BTTS market.
///
/// Run with --dry-run first: it reports team-name match rates without writing.
/// </summary>
public static class ImportOddsCsvCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        using var scope = services.CreateScope();
        var import = scope.ServiceProvider.GetRequiredService<IHistoricalOddsImportService>();

        var dryRun = CommandArgs.Flag(args, "--dry-run");
        var seasons = ParseSeasons(args);

        if (seasons.Count == 0)
        {
            Console.Error.WriteLine("No seasons resolved. Use --seasons=2021,2022 or --from-season=2021.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== HISTORICAL ODDS IMPORT (football-data.co.uk) ===");
        Console.WriteLine($"  Seasons : {string.Join(", ", seasons.Order())}");
        Console.WriteLine($"  Mode    : {(dryRun ? "DRY RUN — nothing is written" : "WRITING")}");
        Console.WriteLine("  Markets : Bet365 1X2 + Over/Under 2.5 (no BTTS in these files)");
        Console.WriteLine();

        var result = await import.ImportAsync(seasons, dryRun);

        Console.WriteLine($"  {"league",-8}{"div",-6}{"season",-8}{"rows",-7}{"matched",-9}" +
                          $"{"priced",-8}{"had odds",-10}{"no fixture",-11}unknown names");

        foreach (var s in result.Seasons.Where(s => s.CsvRows > 0 || s.Error is not null))
        {
            if (s.Error is not null)
            {
                Console.WriteLine($"  {s.LeagueId,-8}{s.Division,-6}{s.Season,-8}(not published)");
                continue;
            }

            Console.WriteLine($"  {s.LeagueId,-8}{s.Division,-6}{s.Season,-8}{s.CsvRows,-7}{s.FixturesMatched,-9}" +
                              $"{s.FixturesPriced,-8}{s.AlreadyPriced,-10}{s.FixtureNotFound,-11}" +
                              $"{s.UnmatchedTeamNames.Count}");
        }

        Console.WriteLine();
        Console.WriteLine($"  CSV rows read           : {result.CsvRows}");
        Console.WriteLine($"  Fixtures newly priced   : {result.FixturesPriced}");
        Console.WriteLine($"  Already had a price     : {result.AlreadyPriced}  (never overwritten)");
        Console.WriteLine($"  No matching fixture     : {result.FixtureNotFound}");

        if (result.UnmatchedTeamNames.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  === {result.UnmatchedTeamNames.Count} TEAM NAMES COULD NOT BE MATCHED ===");
            Console.WriteLine("  These fixtures were skipped rather than guessed. Add them to the alias");
            Console.WriteLine("  table in TeamNameMatcher and re-run — the import is safe to repeat.");
            Console.WriteLine();

            foreach (var name in result.UnmatchedTeamNames)
                Console.WriteLine($"    {name}");
        }

        Console.WriteLine();
        Console.WriteLine(dryRun
            ? "  Dry run complete. Re-run without --dry-run to write these prices."
            : "  Import complete. Run 'odds-coverage', then re-run the backtest.");

        return 0;
    }

    /// <summary>
    /// Seasons are start years: 2025 means the 2025/26 files. Defaults to every
    /// season the database plausibly covers.
    /// </summary>
    private static List<int> ParseSeasons(string[] args)
    {
        var explicitSeasons = CommandArgs.String(args, "--seasons");
        if (explicitSeasons is not null)
        {
            return [.. explicitSeasons
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, CultureInfo.InvariantCulture, out var y) ? y : 0)
                .Where(y => y > 2000)
                .Distinct()];
        }

        var currentSeason = DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        var fromSeason = int.TryParse(CommandArgs.String(args, "--from-season"), CultureInfo.InvariantCulture, out var from)
            ? from
            : currentSeason - 5;

        return [.. Enumerable.Range(fromSeason, Math.Max(1, currentSeason - fromSeason + 1))];
    }
}
