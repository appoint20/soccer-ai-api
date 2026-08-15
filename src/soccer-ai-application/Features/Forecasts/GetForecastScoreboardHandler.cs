using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Forecasts;

/// <summary>
/// Scores every forecaster over the same settled fixtures.
///
/// The pipeline's probabilities come from the frozen copy stored on each row,
/// not from a fresh computation. The model recalibrates continuously, so
/// scoring a months-old model forecast against today's pipeline would compare
/// an old forecast against one that has since seen more results — the pipeline
/// would win on bookkeeping rather than on skill.
/// </summary>
public sealed class GetForecastScoreboardHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetForecastScoreboardQuery, GetForecastScoreboardResponse>
{
    /// <summary>
    /// Below this, variance dominates and a ranking is noise. Deliberately not
    /// a hard filter — the numbers are still returned, just flagged.
    /// </summary>
    private const int MinSampleForVerdict = 50;

    public async Task<GetForecastScoreboardResponse> Handle(
        IReceiveContext<GetForecastScoreboardQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;

        var rows = await LoadSettledAsync(query, cancellationToken);

        if (rows.Count == 0)
        {
            return new GetForecastScoreboardResponse
            {
                From = query.From,
                To = query.To,
                SettledFixtures = 0,
                Forecasters = [],
                Leader = null,
            };
        }

        var forecasters = new List<ForecasterScoreDto>
        {
            // One pipeline row per fixture, not per model: the same fixture
            // appears once per model, and counting it twice would silently
            // weight fixtures by how many models happened to forecast them.
            ScoreSystem([.. rows.GroupBy(r => r.FixtureId).Select(g => g.First())]),
        };

        forecasters.AddRange(rows
            .GroupBy(r => r.Model)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => ScoreModel(g.Key, [.. g])));

        return new GetForecastScoreboardResponse
        {
            From = query.From,
            To = query.To,
            SettledFixtures = rows.Select(r => r.FixtureId).Distinct().Count(),
            Forecasters = forecasters,
            Leader = PickLeader(forecasters),
        };
    }

    private async Task<List<ModelForecast>> LoadSettledAsync(
        GetForecastScoreboardQuery query, CancellationToken cancellationToken)
    {
        var q = dbContext.ModelForecasts
            .AsNoTracking()
            .Where(f => f.SettledAtUtc != null
                        && f.ActualHomeGoals != null
                        && f.ActualAwayGoals != null);

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(f => f.KickoffUtc >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
            q = q.Where(f => f.KickoffUtc < end);
        }

        return await q.ToListAsync(cancellationToken);
    }

    private static ForecasterScoreDto ScoreModel(string model, IReadOnlyList<ModelForecast> rows) =>
        Score(model, rows,
            over25: r => r.Over25Probability,
            btts: r => r.BttsProbability,
            expectedGoals: r => r.ExpectedGoals);

    private static ForecasterScoreDto ScoreSystem(IReadOnlyList<ModelForecast> rows) =>
        Score("system", rows,
            over25: r => r.SystemOver25Probability,
            btts: r => r.SystemBttsProbability,
            expectedGoals: r => r.SystemExpectedGoals);

    private static ForecasterScoreDto Score(
        string name,
        IReadOnlyList<ModelForecast> rows,
        Func<ModelForecast, double> over25,
        Func<ModelForecast, double> btts,
        Func<ModelForecast, double> expectedGoals) => new()
        {
            Forecaster = name,
            SettledFixtures = rows.Count,
            Markets =
            [
                ScoreMarket("over_2_5", rows, over25, r => r.ActualOver25 == true),
                ScoreMarket("btts", rows, btts, r => r.ActualBtts == true),
            ],
            GoalsMae = rows.Count == 0
                ? null
                : Math.Round(rows.Average(r => Math.Abs(expectedGoals(r) - (r.ActualTotalGoals ?? 0))), 3),
            SampleTooSmall = rows.Count < MinSampleForVerdict,
        };

    private static ForecastMarketScoreDto ScoreMarket(
        string market,
        IReadOnlyList<ModelForecast> rows,
        Func<ModelForecast, double> probability,
        Func<ModelForecast, bool> outcome)
    {
        var n = rows.Count;

        return new ForecastMarketScoreDto
        {
            Market = market,

            // Brier: mean squared error against the 0/1 outcome.
            BrierScore = Math.Round(
                rows.Average(r => Math.Pow(probability(r) - (outcome(r) ? 1.0 : 0.0), 2)), 4),

            HitRate = Math.Round(
                rows.Count(r => probability(r) >= 0.5 == outcome(r)) / (double)n, 4),

            MeanProbability = Math.Round(rows.Average(probability), 4),
            BaseRate = Math.Round(rows.Count(outcome) / (double)n, 4),
        };
    }

    /// <summary>
    /// Ranks on mean Brier across markets, and only once someone has a real
    /// sample. Naming a leader on twenty fixtures would be the most misread
    /// number on the page.
    /// </summary>
    private static string? PickLeader(IReadOnlyList<ForecasterScoreDto> forecasters)
    {
        var eligible = forecasters.Where(f => !f.SampleTooSmall).ToList();
        if (eligible.Count < 2) return null;

        return eligible
            .OrderBy(f => f.Markets.Average(m => m.BrierScore))
            .First()
            .Forecaster;
    }
}
