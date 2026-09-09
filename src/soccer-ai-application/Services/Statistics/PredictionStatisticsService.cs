using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services.Statistics;

public sealed record MarketStatistic(string Market, int Predictions, int Correct, double? Accuracy, int PositivePicks,
    int CorrectPositivePicks, double? PositivePrecision, int NegativePicks, int CorrectNegativePicks, double? NegativePrecision,
    int FalsePositives, int FalseNegatives, double? BrierScore, double? Lower95, double? Upper95);
public sealed record LeagueStatistic(int LeagueId, string League, int Matches, double? MeanMarketAccuracy, List<MarketStatistic> Markets);
public sealed record ErrorBreakdown(int BttsPickedButNoBttsOver25, int Under25PickedButOver25, int BttsPickedButNeither, int TotalBttsErrors);
public sealed record PredictionStatistics(DateTimeOffset From, DateTimeOffset Through, string Method, int EligibleFinishedMatches,
    int ScoredMatches, int MissingPreMatchPrediction, int PendingPredictions, List<MarketStatistic> Markets,
    List<LeagueStatistic> Leagues, ErrorBreakdown Errors, string[] ModelVersions, string Note);

public sealed class PredictionStatisticsService(IApplicationDbContext db, TimeProvider? clock = null)
{
    public async Task<PredictionStatistics> GetAsync(int days, CancellationToken ct = default)
    {
        var through = (clock ?? TimeProvider.System).GetUtcNow();
        var periodStart = through.AddDays(-Math.Clamp(days, 7, 730));
        var fixtures = await db.Fixtures.AsNoTracking().Where(f => f.Date >= periodStart && f.Date <= through && f.Status == "FT").ToListAsync(ct);
        var records = await db.PredictionSnapshots.AsNoTracking().Where(p => p.KickoffUtc >= periodStart && p.KickoffUtc <= through).ToListAsync(ct);
        var paired = Pair(fixtures, records);
        var markets = Metrics(paired);
        var leagues = paired.GroupBy(x => x.Fixture.LeagueId).Select(g =>
        {
            var m = Metrics(g.ToList());
            return new LeagueStatistic(g.Key, LeagueCatalog.Name(g.Key), g.Count(), m.Average(x => x.Accuracy), m);
        }).OrderByDescending(l => l.Matches).ToList();
        var pending = await (from p in db.PredictionSnapshots
            join f in db.Fixtures on p.FixtureId equals f.Id
            where f.Date >= periodStart && (f.Status == "NS" || f.Status == "1H" || f.Status == "HT" || f.Status == "2H" || f.Status == "LIVE")
                && p.KickoffUtc == f.Date && p.CapturedAtUtc < p.KickoffUtc
            select f.Id).Distinct().CountAsync(ct);
        var ggMisses = paired.Where(x => x.Snapshot.BttsPick && !(x.Fixture.HomeGoal > 0 && x.Fixture.AwayGoal > 0)).ToList();
        var errors = new ErrorBreakdown(ggMisses.Count(x => x.Fixture.HomeGoal + x.Fixture.AwayGoal > 2),
            paired.Count(x => !x.Snapshot.Over25Pick && x.Fixture.HomeGoal + x.Fixture.AwayGoal > 2),
            ggMisses.Count(x => x.Fixture.HomeGoal + x.Fixture.AwayGoal <= 2), ggMisses.Count);
        return new(periodStart, through, "recorded_pre_match_t_minus_1h", fixtures.Count, paired.Count, fixtures.Count - paired.Count, pending,
            markets, leagues, errors, paired.Select(x => x.Snapshot.ModelVersion).Distinct().Order().ToArray(),
            "Latest recorded forecast at least 1h before the actual kickoff; one per FT fixture. Missing history is excluded, never reconstructed. " +
            "Accuracy scores both Yes and No; precision scores the selected side. League mean weights four targets equally; it is not ROI. " +
            "95% Wilson intervals are nominal and do not account for dependence or league selection. Error categories describe outcomes, not proven causes.");
    }

    public static List<(Fixture Fixture, PredictionSnapshot Snapshot)> Pair(IEnumerable<Fixture> fixtures, IEnumerable<PredictionSnapshot> records)
    {
        var byFixture = records.ToLookup(p => p.FixtureId);
        return fixtures.Where(f => f.Status == "FT").Select(f => (Fixture: f, Snapshot: byFixture[f.Id]
            .Where(p => p.KickoffUtc == f.Date && p.CapturedAtUtc <= f.Date.AddHours(-1))
            .OrderByDescending(p => p.CapturedAtUtc).ThenBy(p => p.Id).FirstOrDefault()))
            .Where(x => x.Snapshot != null).Select(x => (x.Fixture, x.Snapshot!)).ToList();
    }

    public static List<MarketStatistic> Metrics(List<(Fixture Fixture, PredictionSnapshot Snapshot)> rows)
    {
        var result = new List<MarketStatistic>();
        foreach (var market in new[] { "1x2", "over25", "btts", "goals23" })
        {
            var outcomes = rows.Select(x =>
            {
                var f = x.Fixture; var p = x.Snapshot;
                if (market == "1x2")
                {
                    var probabilities = new[] { p.Home, p.Draw, p.Away };
                    var actual = f.HomeGoal > f.AwayGoal ? "home" : f.HomeGoal == f.AwayGoal ? "draw" : "away";
                    var actualIndex = actual == "home" ? 0 : actual == "draw" ? 1 : 2;
                    var win = p.Winner == actual;
                    return (Hit: win, Pick: true, Actual: win, Brier: probabilities.Select((v, i) => Math.Pow(v - (i == actualIndex ? 1 : 0), 2)).Sum());
                }
                var probability = market == "over25" ? p.Over25 : market == "btts" ? p.Btts : p.Goals23;
                var picked = market == "over25" ? p.Over25Pick : market == "btts" ? p.BttsPick : p.Goals23Pick;
                var yes = market == "over25" ? f.HomeGoal + f.AwayGoal > 2 : market == "btts" ? f.HomeGoal > 0 && f.AwayGoal > 0 : f.HomeGoal + f.AwayGoal is 2 or 3;
                return (Hit: picked == yes, Pick: picked, Actual: yes, Brier: Math.Pow(probability - (yes ? 1 : 0), 2));
            }).ToList();
            var n = outcomes.Count; var correct = outcomes.Count(x => x.Hit);
            var picks = outcomes.Count(x => x.Pick); var hits = outcomes.Count(x => x.Pick && x.Actual);
            var negativeHits = outcomes.Count(x => !x.Pick && !x.Actual);
            var interval = Wilson(correct, n);
            result.Add(new(market, n, correct, Rate(correct, n), picks, hits, Rate(hits, picks), n - picks, negativeHits,
                Rate(negativeHits, n - picks), outcomes.Count(x => x.Pick && !x.Actual), outcomes.Count(x => !x.Pick && x.Actual),
                n == 0 ? null : outcomes.Average(x => x.Brier), interval.Lower, interval.Upper));
        }
        return result;
    }
    private static double? Rate(int hits, int count) => count == 0 ? null : (double)hits / count;
    public static (double? Lower, double? Upper) Wilson(int hits, int n)
    {
        if (n == 0) return (null, null);
        const double z = 1.959963984540054;
        var p = (double)hits / n; var denominator = 1 + z * z / n;
        var centre = (p + z * z / (2 * n)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4d * n * n)) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }
}
