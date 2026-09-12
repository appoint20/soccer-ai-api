using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Services.Statistics;

public sealed record ForecastAccuracy(int Predictions, int Correct, double? Accuracy, double? Lower95, double? Upper95,
    double? BrierScore, double? LogLoss, double? MeanProbability, double? BaseRate);
public sealed record PairedForecastMarket(string Market, ForecastAccuracy System, ForecastAccuracy Ai,
    int ImprovedCalls, int WorsenedCalls, double? BrierDifference);
public sealed record RecordedAiForecastComparison(string Model, int Records, int ComparableFixtures,
    int UnfinishedOrMissingFixture, int InvalidTiming, int MissingSystemInputs, int InvalidAiInputs, int DuplicateRecords,
    DateTimeOffset? FirstKickoff, DateTimeOffset? LastKickoff, List<PairedForecastMarket> Markets);
public sealed record RecordedAiForecastReport(string Method, string Note, List<RecordedAiForecastComparison> Models);

/// <summary>Scores recorded AI-adjusted probabilities against their contemporaneous statistical inputs.</summary>
public static class RecordedAiForecastStatistics
{
    public const string Method = "recorded_ai_forecasts_paired_t_minus_1h";
    public const string Note = "Each AI model is compared with its stored statistical inputs on identical FT fixtures, " +
        "recorded at least 1h before the actual kickoff; changed kickoffs and duplicate fixture/model records are excluded. " +
        "Legacy zero or unit statistical probabilities are excluded because missing inputs were stored as zero. " +
        "AI probabilities must be finite and within [0,1]. Accuracy scores both Yes and No at 0.5, not selected bets. " +
        "The ledger does not identify the statistical generation or AI prompt/input hashes; this is historical evidence, " +
        "not validation of the current ML ensemble or current AI pick policy. Wilson intervals are nominal, ignore " +
        "match dependence and are not intervals for the paired improvement. Different AI models can have different cohorts. " +
        "These results do not measure betting profit.";

    public static bool HasSystemInputs(ModelForecast r) => Interior(r.SystemOver25Probability) && Interior(r.SystemBttsProbability);
    public static bool HasAiInputs(ModelForecast r) => double.IsFinite(r.Over25Probability) && r.Over25Probability is >= 0 and <= 1
        && double.IsFinite(r.BttsProbability) && r.BttsProbability is >= 0 and <= 1 && !string.IsNullOrWhiteSpace(r.Model);
    private static bool Interior(double p) => double.IsFinite(p) && p is > 0 and < 1;
    public static bool HasPreMatchTiming(ModelForecast r, Fixture f) => r.PredictedAtUtc != default &&
        r.KickoffUtc == f.Date && r.PredictedAtUtc <= f.Date.AddHours(-1);

    public static List<(Fixture Fixture, ModelForecast Forecast)> Pair(IEnumerable<Fixture> fixtures, IEnumerable<ModelForecast> rows)
    {
        var lookup = fixtures.Where(f => f.Status == "FT" && f.HomeGoal >= 0 && f.AwayGoal >= 0).ToDictionary(f => f.Id);
        return rows.GroupBy(r => (r.FixtureId, r.Model)).Where(g => g.Count() == 1).Select(g => g.Single())
            .Where(r => lookup.TryGetValue(r.FixtureId, out var f) && HasPreMatchTiming(r, f) && HasSystemInputs(r) && HasAiInputs(r))
            .Select(r => (lookup[r.FixtureId], r)).ToList();
    }

    public static RecordedAiForecastReport Build(IEnumerable<Fixture> fixtures, IEnumerable<ModelForecast> records)
    {
        var lookup = fixtures.ToDictionary(f => f.Id);
        var comparisons = new List<RecordedAiForecastComparison>();
        foreach (var model in records.GroupBy(r => r.Model).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var duplicate = 0; var unfinished = 0; var timing = 0; var system = 0; var ai = 0;
            var pairs = new List<(Fixture Fixture, ModelForecast Forecast)>();
            foreach (var group in model.GroupBy(r => r.FixtureId))
            {
                if (group.Count() != 1) { duplicate += group.Count(); continue; }
                var r = group.Single();
                if (!lookup.TryGetValue(r.FixtureId, out var f) || f.Status != "FT" || f.HomeGoal < 0 || f.AwayGoal < 0)
                    { unfinished++; continue; }
                if (!HasPreMatchTiming(r, f)) { timing++; continue; }
                if (!HasSystemInputs(r)) { system++; continue; }
                if (!HasAiInputs(r)) { ai++; continue; }
                pairs.Add((f, r));
            }
            var markets = new List<PairedForecastMarket>();
            foreach (var market in new[] { "over25", "btts" })
            {
                var scored = pairs.Select(p => (
                    System: market == "over25" ? p.Forecast.SystemOver25Probability : p.Forecast.SystemBttsProbability,
                    Ai: market == "over25" ? p.Forecast.Over25Probability : p.Forecast.BttsProbability,
                    Actual: market == "over25" ? p.Fixture.HomeGoal + p.Fixture.AwayGoal > 2 : p.Fixture.HomeGoal > 0 && p.Fixture.AwayGoal > 0)).ToList();
                var s = Score(scored.Select(r => (r.System, r.Actual)).ToList());
                var a = Score(scored.Select(r => (r.Ai, r.Actual)).ToList());
                markets.Add(new(market, s, a,
                    scored.Count(r => (r.Ai >= .5) == r.Actual && (r.System >= .5) != r.Actual),
                    scored.Count(r => (r.Ai >= .5) != r.Actual && (r.System >= .5) == r.Actual), a.BrierScore - s.BrierScore));
            }
            comparisons.Add(new(model.Key, model.Count(), pairs.Count, unfinished, timing, system, ai, duplicate,
                pairs.Select(p => (DateTimeOffset?)p.Fixture.Date).Min(), pairs.Select(p => (DateTimeOffset?)p.Fixture.Date).Max(), markets));
        }
        return new(Method, Note, comparisons);
    }

    public static ForecastAccuracy Score(IReadOnlyList<(double Probability, bool Actual)> rows)
    {
        var n = rows.Count;
        var correct = rows.Count(r => (r.Probability >= .5) == r.Actual);
        var (lower, upper) = PredictionStatisticsService.Wilson(correct, n);
        return new(n, correct, n == 0 ? null : (double)correct / n, lower, upper,
            n == 0 ? null : rows.Average(r => Math.Pow(r.Probability - (r.Actual ? 1 : 0), 2)),
            n == 0 ? null : rows.Average(r => -Math.Log(Math.Clamp(r.Actual ? r.Probability : 1 - r.Probability, 1e-15, 1))),
            n == 0 ? null : rows.Average(r => r.Probability), n == 0 ? null : rows.Count(r => r.Actual) / (double)n);
    }
}
