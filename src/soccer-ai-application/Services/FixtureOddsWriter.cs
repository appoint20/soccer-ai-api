using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services;

/// <summary>
/// Writes best-of-market prices onto a fixture.
///
/// Shared by the live sync and the backfill so the one rule that matters here
/// cannot diverge between them: a market missing from this fetch must never
/// erase a price captured earlier. Overwriting a real captured price with null
/// silently destroys history that cannot be re-fetched.
/// </summary>
public static class FixtureOddsWriter
{
    /// <summary>Applies each present price, leaving absent markets untouched.</summary>
    /// <returns>True when at least one market was written.</returns>
    public static bool ApplyBestPrices(Fixture fixture, FixtureOdds best)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(best);

        var wrote =
            best.HomeWin is not null || best.Draw is not null || best.AwayWin is not null ||
            best.Over25 is not null || best.Under25 is not null || best.BttsYes is not null;

        fixture.HomeWinOdds = best.HomeWin ?? fixture.HomeWinOdds;
        fixture.DrawOdds = best.Draw ?? fixture.DrawOdds;
        fixture.AwayWinOdds = best.AwayWin ?? fixture.AwayWinOdds;
        fixture.Over25Odds = best.Over25 ?? fixture.Over25Odds;
        fixture.Under25Odds = best.Under25 ?? fixture.Under25Odds;
        fixture.BttsYesOdds = best.BttsYes ?? fixture.BttsYesOdds;

        if (wrote) fixture.UpdatedAt = DateTimeOffset.UtcNow;

        return wrote;
    }

    /// <summary>
    /// A fixture the value gate can price at all. Without a guard-valid price
    /// there is no EV, so such a fixture is invisible to the gate no matter how
    /// confident the model is.
    /// </summary>
    public static bool HasAnyValidPrice(
        double? homeWin, double? draw, double? awayWin,
        double? over25, double? under25, double? bttsYes) =>
        OddsGuard.IsValid(homeWin) || OddsGuard.IsValid(draw) || OddsGuard.IsValid(awayWin) ||
        OddsGuard.IsValid(over25) || OddsGuard.IsValid(under25) || OddsGuard.IsValid(bttsYes);
}
