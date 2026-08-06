using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Recovers odds for fixtures the routine sync never priced.
///
/// The sync only spends calls inside a short lookback window, so any fixture
/// that aged past it while the worker was down stays permanently unpriced —
/// and an unpriced fixture cannot be value-checked at all. This service closes
/// that gap with real quoted prices only.
///
/// It is deliberately cautious with quota: newest fixtures first (most likely
/// to still be priced, and most useful), a hard call ceiling, and it yields as
/// soon as the daily budget gets tight.
/// </summary>
public sealed class OddsBackfillService(
    IApiFootballService apiService,
    IApplicationDbContext dbContext,
    ILeagueTierService leagueTiers,
    IApiQuotaTracker quota,
    IOptions<OddsSyncOptions> options,
    ILogger<OddsBackfillService> logger) : IOddsBackfillService
{
    /// <summary>Flush to the database every N fixtures so a stop loses nothing.</summary>
    private const int SaveBatchSize = 25;

    public async Task<OddsBackfillProbe> ProbeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int sampleSize, CancellationToken ct = default)
    {
        var missing = await LoadUnpricedAsync(fromUtc, toUtc, ct);
        if (missing.Count == 0) return new OddsBackfillProbe(0, 0);

        // Spread the sample evenly across the window: odds availability decays
        // with age, so probing only the newest fixtures would flatter the
        // estimate and probing only the oldest would condemn it.
        var step = Math.Max(1, missing.Count / Math.Max(1, sampleSize));
        var sample = missing.Where((_, i) => i % step == 0).Take(sampleSize).ToList();

        var priced = 0;
        foreach (var fixture in sample)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryPriceAsync(fixture, ct)) priced++;
            await Task.Delay(quota.SuggestedDelay, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("[OddsBackfill] Probe: {Priced}/{Sampled} sampled fixtures still priced by the API",
            priced, sample.Count);

        return new OddsBackfillProbe(sample.Count, priced);
    }

    public async Task<OddsBackfillResult> BackfillAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxCalls, CancellationToken ct = default)
    {
        var opt = options.Value;
        var missing = await LoadUnpricedAsync(fromUtc, toUtc, ct);

        if (missing.Count == 0)
            return new OddsBackfillResult(0, 0, 0, 0, OddsBackfillResult.Completed);

        logger.LogInformation("[OddsBackfill] {Missing} unpriced fixtures between {From:yyyy-MM-dd} and {To:yyyy-MM-dd}",
            missing.Count, fromUtc, toUtc);

        var probe = await ProbeAsync(fromUtc, toUtc, opt.BackfillProbeSize, ct);
        if (probe.Sampled > 0 && probe.HitRate < opt.BackfillMinProbeHitRate)
        {
            // Spending hundreds of calls on a window the API no longer prices
            // is the expensive failure this guard exists to prevent.
            logger.LogWarning(
                "[OddsBackfill] Aborting: only {Rate:P0} of sampled fixtures are still priced (floor {Floor:P0}). "
                + "API-Football does not serve odds this far back on this plan.",
                probe.HitRate, opt.BackfillMinProbeHitRate);

            return new OddsBackfillResult(
                missing.Count, probe.Sampled, probe.Priced, probe.Sampled, OddsBackfillResult.ProbeTooLow);
        }

        int attempted = 0, filled = probe.Priced, calls = probe.Sampled;
        var stopReason = OddsBackfillResult.Completed;

        // Newest first: those are both the likeliest to still be priced and the
        // most valuable, since recent form drives current predictions.
        foreach (var fixture in missing.OrderByDescending(f => f.Date))
        {
            if (ct.IsCancellationRequested) { stopReason = OddsBackfillResult.Cancelled; break; }
            if (calls >= maxCalls) { stopReason = OddsBackfillResult.MaxCallsReached; break; }
            if (quota.IsDailyQuotaCritical) { stopReason = OddsBackfillResult.QuotaCritical; break; }

            // Already handled while probing.
            if (FixtureOddsWriter.HasAnyValidPrice(
                    fixture.HomeWinOdds, fixture.DrawOdds, fixture.AwayWinOdds,
                    fixture.Over25Odds, fixture.Under25Odds, fixture.BttsYesOdds))
                continue;

            attempted++;
            calls++;
            if (await TryPriceAsync(fixture, ct)) filled++;

            if (attempted % SaveBatchSize == 0)
                await dbContext.SaveChangesAsync(ct);

            await Task.Delay(quota.SuggestedDelay, CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "[OddsBackfill] Filled {Filled}/{Attempted} attempted ({Calls} calls). Stop reason: {Reason}",
            filled, attempted, calls, stopReason);

        return new OddsBackfillResult(missing.Count, attempted, filled, calls, stopReason);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fixtures in scope with no usable price on any market. "Usable" means
    /// guard-valid: a locale-corrupted 185 counts as missing, because it is.
    /// </summary>
    private async Task<List<Fixture>> LoadUnpricedAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var scopedLeagueIds = leagueTiers.GetSyncLeagueIds().ToList();

        var candidates = await dbContext.Fixtures
            .Where(f => f.Date >= fromUtc && f.Date <= toUtc && scopedLeagueIds.Contains(f.LeagueId))
            .ToListAsync(ct);

        // The guard is client-side maths, so the validity filter runs here
        // rather than being translated into SQL.
        return candidates
            .Where(f => !FixtureOddsWriter.HasAnyValidPrice(
                f.HomeWinOdds, f.DrawOdds, f.AwayWinOdds,
                f.Over25Odds, f.Under25Odds, f.BttsYesOdds))
            .OrderByDescending(f => f.Date)
            .ToList();
    }

    /// <summary>Fetches and stores real quotes. Returns false when the API has none.</summary>
    private async Task<bool> TryPriceAsync(Fixture fixture, CancellationToken ct)
    {
        try
        {
            var quotes = await apiService.GetFixtureOddsQuotesAsync(fixture.ApiId);
            if (quotes.Count == 0) return false;

            var stored = await dbContext.FixtureOddsQuotes
                .Where(q => q.FixtureId == fixture.Id)
                .Select(q => new { q.Bookmaker, q.Market, q.Price })
                .ToListAsync(ct);

            var latestStored = stored
                .GroupBy(q => (q.Bookmaker, q.Market))
                .Select(g => (g.Key.Bookmaker, g.Key.Market, g.Last().Price))
                .ToList();

            foreach (var quote in OddsQuoteAggregator.NewOrChanged(quotes, latestStored))
            {
                dbContext.FixtureOddsQuotes.Add(new FixtureOddsQuote
                {
                    FixtureId = fixture.Id,
                    Bookmaker = quote.Bookmaker,
                    Market = quote.Market,
                    Price = quote.Price,
                    CapturedAtUtc = DateTimeOffset.UtcNow
                });
            }

            return FixtureOddsWriter.ApplyBestPrices(fixture, OddsQuoteAggregator.BestPrices(quotes));
        }
        catch (Exception ex)
        {
            // One unavailable fixture must not end the run.
            logger.LogWarning(ex, "[OddsBackfill] Could not price fixture {FixtureId}", fixture.Id);
            return false;
        }
    }
}
