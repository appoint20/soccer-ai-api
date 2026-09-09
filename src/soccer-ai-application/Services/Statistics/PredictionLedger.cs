using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Statistics;

public sealed class PredictionLedger(IApplicationDbContext db, TimeProvider? clock = null)
{
    public async Task RecordAsync(Fixture fixture, WeightedPrediction prediction, WeightedPrediction raw,
        string modelVersion, string contextJson, CancellationToken ct = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (fixture.Status != "NS" || fixture.Date <= now || fixture.Date > now.AddDays(7)) return;
        if (!IsValid(prediction) || !IsValid(raw)) return;
        var window = now.UtcTicks / TimeSpan.FromHours(3).Ticks;
        if (await db.PredictionSnapshots.AnyAsync(p => p.FixtureId == fixture.Id && p.CaptureWindow == window, ct)) return;
        var row = new PredictionSnapshot
        {
            FixtureId = fixture.Id, CapturedAtUtc = now, CaptureWindow = window, KickoffUtc = fixture.Date,
            ModelVersion = modelVersion, ContextJson = contextJson,
            Winner = prediction.MatchWinner, Over25Pick = prediction.Over25, BttsPick = prediction.BTTS, Goals23Pick = prediction.TwoToThreeGoals,
            Home = prediction.HomeProb, Draw = prediction.DrawProb, Away = prediction.AwayProb,
            Over25 = prediction.Over25Prob, Btts = prediction.BTTSProb, Goals23 = prediction.TwoToThreeGoalsProb,
            RawHome = raw.HomeProb, RawDraw = raw.DrawProb, RawAway = raw.AwayProb,
            RawOver25 = raw.Over25Prob, RawBtts = raw.BTTSProb, RawGoals23 = raw.TwoToThreeGoalsProb
        };
        db.PredictionSnapshots.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The unique index makes a simultaneous English/German/worker capture idempotent.
            db.PredictionSnapshots.Remove(row);
            if (!await db.PredictionSnapshots.AnyAsync(p => p.FixtureId == fixture.Id && p.CaptureWindow == window, ct)) throw;
        }
    }
    private static bool IsValid(WeightedPrediction p) =>
        new[] { p.HomeProb, p.DrawProb, p.AwayProb, p.Over25Prob, p.BTTSProb, p.TwoToThreeGoalsProb }
            .All(v => double.IsFinite(v) && v >= 0 && v <= 1) && Math.Abs(p.HomeProb + p.DrawProb + p.AwayProb - 1) < .02;
}
