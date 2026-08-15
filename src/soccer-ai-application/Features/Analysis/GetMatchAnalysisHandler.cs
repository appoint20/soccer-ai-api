using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Application.Features.Analysis;

/// <summary>
/// Pure DB-read analysis endpoint (precompute architecture).
///
/// Snapshots are computed during sync by IAnalysisPrecomputeService and stored
/// in FixtureAnalysis.SnapshotJson; this handler only deserializes them.
/// Model computation happens inside a request ONLY as a fallback when a
/// snapshot is missing or stale (finished fixture with a pre-result snapshot),
/// or when ?refresh=true explicitly forces a recompute.
/// </summary>
public class GetMatchAnalysisHandler(
    FixtureQueryHelper queryHelper,
    IApplicationDbContext dbContext,
    IAnalysisPrecomputeService precomputeService,
    IAiSyncService aiSyncService,
    ILogger<GetMatchAnalysisHandler> logger)
    : IRequestHandler<GetMatchAnalysisQuery, GetMatchAnalysisResponse>
{
    public async Task<GetMatchAnalysisResponse> Handle(
        IReceiveContext<GetMatchAnalysisQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var lang = query.Language ?? "en";
        var date = query.Date ?? DateTimeOffset.UtcNow;
        var limit = query.ResolveLimit();
        var offset = query.ResolveOffset();

        // Step 1: Load fixtures + teams (cheap indexed queries)
        var (fixtures, _, totalCount) = await queryHelper.GetFixturesWithTeamsAsync(
            date, limit, offset, query.OnlyAnalyzed, cancellationToken);

        if (fixtures.Count == 0)
        {
            logger.LogInformation("No fixtures found for {Date}", date.ToString("yyyy-MM-dd"));
            return new GetMatchAnalysisResponse
            {
                Items = [],
                Limit = limit,
                Offset = offset,
                Total = totalCount,
                Summary = new AnalysisSummary { TotalMatches = 0, CorrectMatches = 0, AccuracyRate = 0 }
            };
        }

        // Step 2 (optional, slow, admin use): force AI regeneration first
        if (query.Refresh)
        {
            foreach (var fixture in fixtures)
            {
                try
                {
                    await aiSyncService.SyncSingleFixtureAsync(fixture.Id, force: true, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "AI refresh failed for fixture {Id}", fixture.Id);
                }
            }
        }

        // Step 3: ONE query loads all snapshots for the page
        var fixtureIds = fixtures.Select(f => f.Id).ToList();
        var snapshotRows = await dbContext.FixtureAnalyses
            .AsNoTracking()
            .Where(a => fixtureIds.Contains(a.FixtureId) && a.Lang == lang)
            .ToDictionaryAsync(a => a.FixtureId, cancellationToken);

        // Step 4: deserialize snapshots; recompute only when missing/stale/forced
        var analysisList = new List<MatchAnalysis>(fixtures.Count);
        foreach (var fixture in fixtures)
        {
            try
            {
                var snapshot = query.Refresh
                    ? null
                    : AnalysisSnapshotSerializer.Deserialize(
                        snapshotRows.GetValueOrDefault(fixture.Id)?.SnapshotJson);

                // Stale: the fixture finished after the snapshot was computed.
                var stale = snapshot is { Result: null } && fixture.Status == "FT";

                if (snapshot == null || stale)
                {
                    var recomputed = await precomputeService.RecomputeFixtureAsync(fixture.Id, cancellationToken);
                    snapshot = recomputed.GetValueOrDefault(lang);
                }

                if (snapshot != null)
                    analysisList.Add(snapshot);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to produce analysis for fixture {Id}", fixture.Id);
            }
        }

        // Step 5: model forecasts for this page, so the app can show them under
        // the summary. One extra indexed query rather than a per-fixture read.
        var forecasts = await dbContext.ModelForecasts
            .AsNoTracking()
            .Where(f => fixtureIds.Contains(f.FixtureId))
            .OrderBy(f => f.Model)
            .ToListAsync(cancellationToken);

        var forecastsByFixture = forecasts
            .GroupBy(f => f.FixtureId)
            .ToDictionary(g => g.Key, g => g.Select(f => new MatchModelForecastDto
            {
                Model = f.Model,
                ExpectedGoals = f.ExpectedGoals,
                PredictedScore = $"{f.PredictedHomeGoals}:{f.PredictedAwayGoals}",
                Over25Probability = f.Over25Probability,
                BttsProbability = f.BttsProbability,
                Confidence = f.Confidence,
                Rationale = f.Rationale,
                PredictedAtUtc = f.PredictedAtUtc,
                SystemOver25Probability = f.SystemOver25Probability,
                SystemBttsProbability = f.SystemBttsProbability,
                ActualTotalGoals = f.ActualTotalGoals,
            }).ToList());

        // Step 6: summary over finished matches
        var summary = AnalysisResponseMapper.CalculateSummary(analysisList);

        return new GetMatchAnalysisResponse
        {
            Items = analysisList,
            Limit = limit,
            Offset = offset,
            Total = totalCount,
            ModelForecasts = forecastsByFixture,
            Summary = summary
        };
    }
}
