using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services;

/// <summary>
/// Pure aggregation over per-bookmaker quotes.
/// Best price = the HIGHEST guard-valid price per market — line shopping is
/// free EV; every downstream consumer (calibration, EV gate, backtest) reads
/// the fixture columns which carry these best prices.
/// </summary>
public static class OddsQuoteAggregator
{
    public static FixtureOdds BestPrices(IReadOnlyCollection<OddsQuote> quotes)
    {
        double? Best(string market)
        {
            var valid = quotes
                .Where(q => q.Market == market && OddsGuard.IsValid(q.Price))
                .Select(q => (double?)q.Price)
                .ToList();
            return valid.Count > 0 ? valid.Max() : null;
        }

        return new FixtureOdds(
            Best(OddsMarkets.HomeWin),
            Best(OddsMarkets.Draw),
            Best(OddsMarkets.AwayWin),
            Best(OddsMarkets.Over25),
            Best(OddsMarkets.Under25),
            Best(OddsMarkets.BttsYes),
            Best(OddsMarkets.BttsNo));
    }

    /// <summary>
    /// Quotes worth persisting: the price changed (or is new) relative to the
    /// latest stored quote for the same bookmaker+market. Keeps the first
    /// (opening) and every movement — drift is derivable, storage stays lean.
    /// </summary>
    public static List<OddsQuote> NewOrChanged(
        IReadOnlyCollection<OddsQuote> fetched,
        IReadOnlyCollection<(string Bookmaker, string Market, double Price)> latestStored)
    {
        var latest = latestStored.ToDictionary(x => (x.Bookmaker, x.Market), x => x.Price);
        return fetched
            .Where(q => OddsGuard.IsValid(q.Price))
            .Where(q => !latest.TryGetValue((q.Bookmaker, q.Market), out var price) ||
                        Math.Abs(price - q.Price) > 1e-9)
            .ToList();
    }
}
