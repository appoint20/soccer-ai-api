using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Evaluation;

namespace SoccerAi.Application.Services.Calibration;

/// <summary>
/// Per-market isotonic maps fitted strictly walk-forward:
/// the map used for a fixture in ISO week k is trained ONLY on finished
/// fixtures dated before the Monday of week k. Weekly fits are memoized in a
/// context-scoped cache, isolated by model version and minimum sample count.
/// Mutable post-result analysis caches are never calibration evidence.
/// </summary>
public sealed class ProbabilityCalibrationService(
    IApplicationDbContext dbContext,
    IOptions<CalibrationOptions> options,
    ILogger<ProbabilityCalibrationService> logger) : IProbabilityCalibrationService
{
    public static class Markets
    {
        public const string Btts = "btts";
        public const string Over25 = "over25";
        public const string Under25 = "under25";
        public const string Goals23 = "goals_2_3";
        public const string Winner = "match_winner";
        public const string Draw = "draw";
    }

    private sealed record FittedMarket(IsotonicRegression? Model, int Samples)
    {
        public bool Active => Model is not null;
        public double Predict(double p) => Model?.Predict(p) ?? p;
    }

    // Cache per database context: isolated datasets/options/model generations cannot share a fit.
    private static ConditionalWeakTable<IApplicationDbContext, ConcurrentDictionary<(DateTime Week, int MinSamples, string Version), Task<Dictionary<string, FittedMarket>>>> Caches = new();
    public static void ClearCache() => Caches = new();

    public async Task<CalibrationResult> ApplyAsync(
        WeightedPrediction raw, DateTimeOffset asOf, CancellationToken ct = default, string modelVersion = "")
    {
        var opt = options.Value;
        if (!opt.IsotonicEnabled)
            return PassThrough(raw);

        var weekStart = IsoWeekStartUtc(asOf);
        var cache = Caches.GetOrCreateValue(dbContext);
        var key = (weekStart, opt.IsotonicMinSamples, modelVersion);
        Dictionary<string, FittedMarket> models;
        try
        {
            models = await cache.GetOrAdd(key, k => FitWeekAsync(k.Week, k.MinSamples, k.Version, ct));
        }
        catch (OperationCanceledException)
        {
            cache.TryRemove(key, out _);
            throw;
        }
        catch (Exception ex)
        {
            cache.TryRemove(key, out _);
            logger.LogError(ex, "Isotonic fit failed for week {Week} — passing through", weekStart);
            return PassThrough(raw);
        }

        var rawWinner = Math.Max(raw.HomeProb, raw.AwayProb);
        var winner = models[Markets.Winner];
        var draw = models[Markets.Draw];

        // 1X2: map home/away through the winner model, draw through its own,
        // then renormalize to a proper distribution.
        var h = winner.Predict(raw.HomeProb);
        var a = winner.Predict(raw.AwayProb);
        var d = draw.Predict(raw.DrawProb);
        var sum = h + a + d;
        if (sum > 0) { h /= sum; a /= sum; d /= sum; }

        var over = models[Markets.Over25].Predict(raw.Over25Prob);
        var btts = models[Markets.Btts].Predict(raw.BTTSProb);
        var goals23 = models[Markets.Goals23].Predict(raw.TwoToThreeGoalsProb);

        var calibrated = new WeightedPrediction
        {
            HomeProb = Math.Round(h, 4),
            DrawProb = Math.Round(d, 4),
            AwayProb = Math.Round(a, 4),
            Over25Prob = Math.Round(over, 4),
            Over25 = over > 0.50,
            BTTSProb = Math.Round(btts, 4),
            BTTS = btts > 0.50,
            TwoToThreeGoalsProb = Math.Round(goals23, 4),
            TwoToThreeGoals = goals23 > 0.50,
            MatchWinner = d >= h && d >= a ? "draw" : a > h ? "away" : "home",
            Confidence = Math.Round(Math.Max(d, Math.Max(h, a)), 4)
        };

        var trace = new List<CalibrationTraceEntry>
        {
            new(Markets.Winner, Math.Round(rawWinner, 4), Math.Round(Math.Max(h, a), 4),
                winner.Active, winner.Samples),
            new(Markets.Draw, Math.Round(raw.DrawProb, 4), Math.Round(d, 4), draw.Active, draw.Samples),
            new(Markets.Over25, Math.Round(raw.Over25Prob, 4), Math.Round(over, 4),
                models[Markets.Over25].Active, models[Markets.Over25].Samples),
            new(Markets.Under25, Math.Round(1 - raw.Over25Prob, 4), Math.Round(1 - over, 4),
                models[Markets.Over25].Active, models[Markets.Over25].Samples),
            new(Markets.Btts, Math.Round(raw.BTTSProb, 4), Math.Round(btts, 4),
                models[Markets.Btts].Active, models[Markets.Btts].Samples),
            new(Markets.Goals23, Math.Round(raw.TwoToThreeGoalsProb, 4), Math.Round(goals23, 4),
                models[Markets.Goals23].Active, models[Markets.Goals23].Samples)
        };

        return new CalibrationResult(calibrated, trace);
    }

    private async Task<Dictionary<string, FittedMarket>> FitWeekAsync(DateTime weekStartUtc, int minSamples, string modelVersion, CancellationToken ct)
    {
        var cutoff = new DateTimeOffset(weekStartUtc, TimeSpan.Zero);

        // Analysis caches can be overwritten after results. Only raw forecasts actually
        // recorded before the fixed T-1h horizon may train a probability map.
        var candidates = await (
                from f in dbContext.Fixtures.AsNoTracking()
                join p in dbContext.PredictionSnapshots.AsNoTracking() on f.Id equals p.FixtureId
                where f.Status == "FT" && f.Date < cutoff && p.CapturedAtUtc < cutoff && p.KickoffUtc == f.Date &&
                    (modelVersion == "" || p.ModelVersion == modelVersion)
                select new { p.FixtureId, p.Id, p.CapturedAtUtc, p.KickoffUtc,
                    HomeProb = p.RawHome, DrawProb = p.RawDraw, AwayProb = p.RawAway,
                    Over25Prob = p.RawOver25, BttsProb = p.RawBtts, Goals23Prob = p.RawGoals23,
                    f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);
        var rows = candidates.Where(p => p.CapturedAtUtc <= p.KickoffUtc.AddHours(-1))
            .GroupBy(p => p.FixtureId).Select(g => g.OrderByDescending(p => p.CapturedAtUtc).ThenBy(p => p.Id).First())
            .ToList();

        // Side-win model: pool BOTH sides so the map covers the full probability
        // range. Training only on the favourite (max) probability left the model
        // blind below ~0.33 while it was still being applied to underdog
        // probabilities at prediction time — the v6 overcorrection.
        var winnerSamples = rows
            .Select(r => (r.HomeProb, Won: r.HomeGoal > r.AwayGoal))
            .Concat(rows.Select(r => (r.AwayProb, Won: r.AwayGoal > r.HomeGoal)))
            .Select(s => (s.Item1, s.Won))
            .ToList();

        var models = new Dictionary<string, FittedMarket>
        {
            [Markets.Winner] = FitMarket(winnerSamples, minSamples),
            [Markets.Draw] = FitMarket(rows.Select(r => (r.DrawProb, r.HomeGoal == r.AwayGoal)).ToList(), minSamples),
            [Markets.Over25] = FitMarket(rows.Select(r => (r.Over25Prob, r.HomeGoal + r.AwayGoal > 2)).ToList(), minSamples),
            [Markets.Btts] = FitMarket(rows.Select(r => (r.BttsProb, r.HomeGoal > 0 && r.AwayGoal > 0)).ToList(), minSamples),
            [Markets.Goals23] = FitMarket(rows.Select(r => (r.Goals23Prob, r.HomeGoal + r.AwayGoal is 2 or 3)).ToList(), minSamples)
        };

        logger.LogInformation(
            "[Isotonic] Week {Week}: {N} training rows; active: {Active}",
            weekStartUtc, rows.Count,
            string.Join(", ", models.Where(m => m.Value.Active).Select(m => m.Key)));

        return models;
    }

    private static FittedMarket FitMarket(List<(double P, bool Y)> samples, int minSamples)
    {
        if (samples.Count < minSamples)
            return new FittedMarket(null, samples.Count);

        // Shrinkage inside the model keeps thin probability ranges close to raw.
        var model = IsotonicRegression.Fit(samples.Select(s => (s.P, s.Y)).ToList());
        return new FittedMarket(model, samples.Count);
    }

    private static CalibrationResult PassThrough(WeightedPrediction raw)
    {
        return new CalibrationResult(raw,
        [
            new CalibrationTraceEntry("all", 0, 0, false, 0)
        ]);
    }

    /// <summary>Monday 00:00 UTC of the ISO week containing the date.</summary>
    public static DateTime IsoWeekStartUtc(DateTimeOffset date)
    {
        var utc = date.UtcDateTime.Date;
        var offset = ((int)utc.DayOfWeek + 6) % 7; // Monday=0 ... Sunday=6
        return utc.AddDays(-offset);
    }
}
