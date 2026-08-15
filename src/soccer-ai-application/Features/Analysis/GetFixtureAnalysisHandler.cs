using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Application.Features.Analysis;

/// <summary>
/// Serves one fixture's analysis by id, for any date.
///
/// Same read path as the list endpoint — snapshot first, recompute only when
/// missing or stale — so a match detail screen never depends on the fixture
/// happening to fall inside the day the app last requested.
/// </summary>
public sealed class GetFixtureAnalysisHandler(
    IApplicationDbContext dbContext,
    IAnalysisPrecomputeService precomputeService,
    ILogger<GetFixtureAnalysisHandler> logger)
    : IRequestHandler<GetFixtureAnalysisQuery, GetFixtureAnalysisResponse>
{
    public async Task<GetFixtureAnalysisResponse> Handle(
        IReceiveContext<GetFixtureAnalysisQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var lang = string.IsNullOrWhiteSpace(query.Language) ? "en" : query.Language;

        var fixture = await dbContext.Fixtures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == query.FixtureId, cancellationToken);

        if (fixture is null)
        {
            logger.LogInformation("Fixture {FixtureId} not found", query.FixtureId);
            return new GetFixtureAnalysisResponse();
        }

        var row = await dbContext.FixtureAnalyses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.FixtureId == query.FixtureId && a.Lang == lang, cancellationToken);

        var snapshot = query.Refresh
            ? null
            : AnalysisSnapshotSerializer.Deserialize(row?.SnapshotJson);

        // Stale: the fixture finished after the snapshot was taken, so the
        // snapshot still has no result and the detail screen would show a
        // finished match with no score.
        var stale = snapshot is { Result: null } && IsFinished(fixture.Status);

        if (snapshot is null || stale)
        {
            var recomputed = await precomputeService.RecomputeFixtureAsync(query.FixtureId, cancellationToken);
            snapshot = recomputed.GetValueOrDefault(lang);
        }

        var forecasts = await dbContext.ModelForecasts
            .AsNoTracking()
            .Where(f => f.FixtureId == query.FixtureId)
            .OrderBy(f => f.Model)
            .Select(f => new MatchModelForecastDto
            {
                Model = f.Model,
                ExpectedGoals = f.ExpectedGoals,
                PredictedScore = f.PredictedHomeGoals + ":" + f.PredictedAwayGoals,
                Over25Probability = f.Over25Probability,
                BttsProbability = f.BttsProbability,
                Confidence = f.Confidence,
                Rationale = f.Rationale,
                PredictedAtUtc = f.PredictedAtUtc,
                SystemOver25Probability = f.SystemOver25Probability,
                SystemBttsProbability = f.SystemBttsProbability,
                ActualTotalGoals = f.ActualHomeGoals + f.ActualAwayGoals,
            })
            .ToListAsync(cancellationToken);

        return new GetFixtureAnalysisResponse { Match = snapshot, ModelForecasts = forecasts };
    }

    private static bool IsFinished(string status) =>
        status is "FT" or "AET" or "PEN" or "ABD" or "AWD" or "WO";
}
