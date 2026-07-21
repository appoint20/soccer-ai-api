using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services;

/// <summary>Opening-vs-latest line movement derived from timestamped quotes.</summary>
public sealed record OddsDriftResult(
    double? FavoriteDriftPct,
    double? Over25DriftPct,
    string FavoriteDirection);

/// <summary>
/// Pure drift computation over per-bookmaker quote history.
/// Opening = best guard-valid price within the FIRST capture window per market;
/// latest = best within the LAST capture window. Negative favorite drift =
/// price shortening = money arriving on the favorite.
/// </summary>
public static class OddsDriftCalculator
{
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromMinutes(10);

    public static OddsDriftResult? Compute(IReadOnlyCollection<FixtureOddsQuote> quotes)
    {
        var valid = quotes.Where(q => OddsGuard.IsValid(q.Price)).ToList();
        if (valid.Count == 0) return null;

        double? DriftFor(string market)
        {
            var marketQuotes = valid.Where(q => q.Market == market).ToList();
            if (marketQuotes.Count == 0) return null;

            var first = marketQuotes.Min(q => q.CapturedAtUtc);
            var last = marketQuotes.Max(q => q.CapturedAtUtc);
            if (last - first < CaptureWindow) return null; // single snapshot — no drift measurable

            var opening = marketQuotes.Where(q => q.CapturedAtUtc <= first + CaptureWindow).Max(q => q.Price);
            var latest = marketQuotes.Where(q => q.CapturedAtUtc >= last - CaptureWindow).Max(q => q.Price);
            return Math.Round((latest - opening) / opening, 4);
        }

        // Favorite = the 1X2 side with the shorter LATEST price.
        double? LatestBest(string market)
        {
            var marketQuotes = valid.Where(q => q.Market == market).ToList();
            if (marketQuotes.Count == 0) return null;
            var last = marketQuotes.Max(q => q.CapturedAtUtc);
            return marketQuotes.Where(q => q.CapturedAtUtc >= last - CaptureWindow).Max(q => q.Price);
        }

        var homeLatest = LatestBest(OddsMarkets.HomeWin);
        var awayLatest = LatestBest(OddsMarkets.AwayWin);

        double? favoriteDrift = null;
        var direction = "unknown";
        if (homeLatest is not null && awayLatest is not null)
        {
            var favoriteMarket = homeLatest <= awayLatest ? OddsMarkets.HomeWin : OddsMarkets.AwayWin;
            favoriteDrift = DriftFor(favoriteMarket);
            direction = favoriteDrift switch
            {
                null => "unknown",
                < 0 => "shortening (money on favorite)",
                > 0 => "drifting out (money against favorite)",
                _ => "flat"
            };
        }

        return new OddsDriftResult(favoriteDrift, DriftFor(OddsMarkets.Over25), direction);
    }
}
