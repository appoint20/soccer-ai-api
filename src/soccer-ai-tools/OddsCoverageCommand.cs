using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace SoccerAi.Tools;

/// <summary>
/// Diagnoses odds coverage per league + season and, where quote history
/// exists, attributes the cause per missing fixture:
/// - never_fetched: no quotes ever captured (typically synced after the
///   pre-match odds window — API-Football serves no historical odds there)
/// - market_missing: bookmaker quotes exist but not for this market
/// - corrupted_legacy: only locale-corrupted values (excluded by the guard)
/// </summary>
public static class OddsCoverageCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var fixtures = await db.Fixtures.AsNoTracking()
            .Where(f => f.Status == "FT")
            .Select(f => new
            {
                f.Id, f.LeagueId, f.Date,
                f.HomeWinOdds, f.DrawOdds, f.AwayWinOdds,
                f.Over25Odds, f.Under25Odds, f.BttsYesOdds
            })
            .ToListAsync();

        var quoteFixtures = await db.FixtureOddsQuotes.AsNoTracking()
            .GroupBy(q => q.FixtureId)
            .Select(g => new { FixtureId = g.Key, Markets = g.Select(q => q.Market).Distinct().ToList() })
            .ToDictionaryAsync(x => x.FixtureId, x => x.Markets);

        Console.WriteLine();
        Console.WriteLine("=== ODDS COVERAGE per league + season (FT fixtures) ===");
        Console.WriteLine($"{"league",-8}{"season",-8}{"n",-6}{"1X2 ok",-8}{"O/U ok",-8}{"BTTS ok",-9}" +
                          $"{"corrupt",-9}{"neverFetched",-13}{"mktMissing",-11}");

        foreach (var group in fixtures
                     .GroupBy(f => (f.LeagueId, Season: f.Date.Month >= 7 ? f.Date.Year : f.Date.Year - 1))
                     .OrderBy(g => g.Key.LeagueId).ThenBy(g => g.Key.Season))
        {
            var rows = group.ToList();
            int ok1X2 = 0, okOu = 0, okBtts = 0, corrupt = 0, neverFetched = 0, marketMissing = 0;

            foreach (var f in rows)
            {
                var has1X2 = OddsGuard.IsValid(f.HomeWinOdds) && OddsGuard.IsValid(f.DrawOdds) && OddsGuard.IsValid(f.AwayWinOdds);
                var hasOu = OddsGuard.IsValid(f.Over25Odds) && OddsGuard.IsValid(f.Under25Odds);
                var hasBtts = OddsGuard.IsValid(f.BttsYesOdds);
                if (has1X2) ok1X2++;
                if (hasOu) okOu++;
                if (hasBtts) okBtts++;

                if (has1X2 && hasOu && hasBtts) continue;

                var anyStored = f.HomeWinOdds ?? f.DrawOdds ?? f.AwayWinOdds ?? f.Over25Odds ?? f.Under25Odds ?? f.BttsYesOdds;
                if (anyStored is not null && !OddsGuard.IsValid(anyStored))
                {
                    corrupt++;
                }
                else if (!quoteFixtures.TryGetValue(f.Id, out var markets) || markets.Count == 0)
                {
                    neverFetched++;
                }
                else
                {
                    marketMissing++;
                }
            }

            string Pct(int c) => rows.Count > 0 ? $"{c * 100.0 / rows.Count:F0}%" : "-";
            Console.WriteLine($"{group.Key.LeagueId,-8}{group.Key.Season,-8}{rows.Count,-6}" +
                              $"{Pct(ok1X2),-8}{Pct(okOu),-8}{Pct(okBtts),-9}" +
                              $"{corrupt,-9}{neverFetched,-13}{marketMissing,-11}");
        }

        Console.WriteLine();
        Console.WriteLine("NOTE: 'neverFetched' fixtures were synced outside the pre-match window —");
        Console.WriteLine("API-Football serves no odds for past fixtures on this endpoint, so those");
        Console.WriteLine("can only be prevented going forward (every sync now line-shops upcoming");
        Console.WriteLine("fixtures across ALL bookmakers).");
        return 0;
    }
}
