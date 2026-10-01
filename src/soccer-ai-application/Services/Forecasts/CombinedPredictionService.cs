using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Forecasts;

public sealed class CombinedPredictionService(IOptions<CombinedPredictionOptions> options) : ICombinedPredictionService
{
    public CombinedPrediction Combine(int fixtureId, DateTimeOffset kickoff, DateTimeOffset capturedAt,
        IReadOnlyList<PredictionSource> sources)
    {
        var weights = new Dictionary<string, double>
        {
            ["historical"] = options.Value.HistoricalWeight, ["ml"] = options.Value.MlWeight,
            ["provider"] = options.Value.ProviderWeight, ["ai"] = options.Value.AiWeight
        };
        if (weights.Values.Any(value => !double.IsFinite(value) || value < 0)
            || !double.IsFinite(weights.Values.Sum()) || weights.Values.Sum() <= 0)
            throw new InvalidOperationException("Combined prediction weights must be finite, non-negative and not all zero.");
        if (capturedAt >= kickoff) throw new InvalidOperationException("Cannot create a pre-match prediction after kickoff.");
        if (sources.Select(source => source.Name).Distinct().Count() != 4 || sources.Count != 4
            || sources.Any(source => !weights.ContainsKey(source.Name)))
            throw new ArgumentException("Exactly one entry for each of historical, ml, provider and ai is required.");
        var eligible = sources.Select(source => source.CapturedAtUtc > capturedAt || source.CapturedAtUtc >= kickoff
            ? source with { Markets = null, UnavailableReason = "Evidence is not available before this decision." } : source).ToArray();
        var outcomeTotal = eligible.Where(source => source.Markets?.HasOutcomes == true).Sum(source => weights[source.Name]);
        var goalsTotal = eligible.Where(source => source.Markets?.HasGoals == true).Sum(source => weights[source.Name]);
        if (outcomeTotal <= 0 || goalsTotal <= 0)
            throw new InvalidOperationException("Insufficient valid sources to predict all requested markets.");
        var applied = eligible.Select(source => source with
        {
            OutcomeWeight = source.Markets?.HasOutcomes == true ? weights[source.Name] / outcomeTotal : 0,
            GoalsWeight = source.Markets?.HasGoals == true ? weights[source.Name] / goalsTotal : 0
        }).ToArray();
        double Outcome(Func<CombinedMarkets, double?> select) => applied.Where(source => source.OutcomeWeight > 0)
            .Sum(source => source.OutcomeWeight * select(source.Markets!)!.Value /
                (source.Markets!.HomeWin!.Value + source.Markets.Draw!.Value + source.Markets.AwayWin!.Value));
        double Goals(Func<CombinedMarkets, double?> select) => applied.Where(source => source.GoalsWeight > 0)
            .Sum(source => source.GoalsWeight * select(source.Markets!)!.Value);
        var home = Outcome(markets => markets.HomeWin);
        var draw = Outcome(markets => markets.Draw);
        return new(fixtureId, kickoff, capturedAt, new CombinedMarkets
        {
            HomeWin = home, Draw = draw, AwayWin = Math.Max(0, 1 - home - draw),
            Btts = Goals(markets => markets.Btts), Over25 = Goals(markets => markets.Over25),
            TwoToThreeGoals = Goals(markets => markets.TwoToThreeGoals),
            BttsAndOver25 = Goals(markets => markets.BttsAndOver25), ExpectedGoals = Goals(markets => markets.ExpectedGoals)
        }, applied);
    }

    public static CombinedMarkets? ProviderMarkets(ProviderPrediction? provider)
    {
        if (provider is null) return null;
        var markets = new CombinedMarkets
        {
            HomeWin = provider.PercentHome, Draw = provider.PercentDraw, AwayWin = provider.PercentAway
        };
        var values = new[] { provider.Home?.GoalsForAverage, provider.Home?.GoalsAgainstAverage,
            provider.Away?.GoalsForAverage, provider.Away?.GoalsAgainstAverage };
        if (provider.Home?.Played >= 3 && provider.Away?.Played >= 3
            && values.All(value => value.HasValue && double.IsFinite(value.Value) && value is >= 0 and <= 10))
        {
            var home = (values[0]!.Value + values[3]!.Value) / 2;
            var away = (values[2]!.Value + values[1]!.Value) / 2;
            var goals = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(home, away, 0, 15));
            markets = markets with
            {
                Btts = goals.Btts, Over25 = goals.Over25, TwoToThreeGoals = goals.TwoToThreeGoals,
                BttsAndOver25 = goals.BttsAndOver25, ExpectedGoals = home + away
            };
        }
        return markets;
    }
}
