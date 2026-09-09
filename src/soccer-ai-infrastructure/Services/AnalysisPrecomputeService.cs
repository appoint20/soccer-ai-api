using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Statistics;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Precomputes the complete /api/analyze response per fixture+language and
/// stores it in FixtureAnalysis.SnapshotJson. The HTTP read path only
/// deserializes — models never run inside a request.
/// </summary>
public sealed class AnalysisPrecomputeService(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    ILeagueTierService leagueTiers,
    PredictionLedger predictionLedger,
    IGoalRateForecaster goalRateForecaster,
    ILogger<AnalysisPrecomputeService> logger) : IAnalysisPrecomputeService
{
    private static readonly string[] Languages = ["en", "de"];

    public async Task<IReadOnlyDictionary<string, MatchAnalysis>> RecomputeFixtureAsync(
        int fixtureId, CancellationToken ct = default)
    {
        var fixture = await dbContext.Fixtures.FirstOrDefaultAsync(f => f.Id == fixtureId, ct);
        if (fixture == null)
        {
            logger.LogWarning("[Precompute] Fixture {Id} not found", fixtureId);
            return new Dictionary<string, MatchAnalysis>();
        }

        return await RecomputeAsync(fixture, ct);
    }

    public async Task<int> RecomputeWindowAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default)
    {
        var leagueIds = leagueTiers.GetSyncLeagueIds().ToList();
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc && leagueIds.Contains(f.LeagueId))
            .OrderBy(f => f.Date)
            .ToListAsync(ct);

        logger.LogInformation("[Precompute] Recomputing {Count} in-scope fixtures ({Start:yyyy-MM-dd}..{End:yyyy-MM-dd})",
            fixtures.Count, startUtc, endUtc);

        var done = 0;
        foreach (var fixture in fixtures)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await RecomputeAsync(fixture, ct);
                done++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Precompute] Failed for fixture {Id}", fixture.Id);
            }
        }

        logger.LogInformation("[Precompute] Window complete: {Done}/{Total}", done, fixtures.Count);
        return done;
    }

    private async Task<IReadOnlyDictionary<string, MatchAnalysis>> RecomputeAsync(
        Fixture fixture, CancellationToken ct)
    {
        var teams = await dbContext.Teams
            .Where(t => t.ApiId == fixture.HomeTeamId || t.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(t => t.ApiId, t => t, ct);

        var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
        var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
        if (homeTeam == null || awayTeam == null)
        {
            logger.LogWarning("[Precompute] Teams missing for fixture {Id} — skipped", fixture.Id);
            return new Dictionary<string, MatchAnalysis>();
        }

        var results = new Dictionary<string, MatchAnalysis>();
        FixtureAnalysisResult? scored = null;

        foreach (var lang in Languages)
        {
            // refresh: true → models run fresh; the AI narrative row is still used.
            var analysis = await analysisService.AnalyzeFixtureAsync(fixture, lang, refresh: true, ct);
            // Probabilities do not vary by language — only the narrative does —
            // so the ledger below records once from whichever ran first.
            scored ??= analysis;
            var mapped = AnalysisResponseMapper.MapToResponse(
                fixture, analysis, homeTeam, awayTeam, analysis.Ai);
            results[lang] = mapped;

            // Math cache stores the RAW prediction — it is the isotonic layer's
            // training data; persisting calibrated values would self-correct.
            await UpsertSnapshotAsync(fixture.Id, lang, mapped, analysis.RawPrediction ?? analysis.Prediction, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        await RecordPredictionAsync(fixture, scored, ct);
        return results;
    }

    /// <summary>
    /// Writes the immutable pre-match probabilities the statistics endpoint
    /// scores against.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the snapshot cache written above. That cache
    /// is rewritten on every recompute, so by the time a fixture finishes it
    /// holds whatever the model last thought — which cannot answer "what did we
    /// predict beforehand". The ledger keeps one immutable row per fixture per
    /// capture window and never updates it.
    ///
    /// A failure here must not lose the analysis: the recompute has already
    /// committed, and an unrecorded fixture costs one row of history, whereas a
    /// thrown exception costs the whole precompute run.
    /// </remarks>
    private async Task RecordPredictionAsync(
        Fixture fixture, FixtureAnalysisResult? analysis, CancellationToken ct)
    {
        if (analysis?.Prediction is not { } prediction) return;

        try
        {
            var context = System.Text.Json.JsonSerializer.Serialize(new
            {
                league = fixture.LeagueId,
                // What the gate could actually see when this call was made. A
                // pick made with no price is a different animal from one made
                // against a live market, and the statistics have to be able to
                // tell them apart after the fact.
                priced = new
                {
                    home = fixture.HomeWinOdds,
                    draw = fixture.DrawOdds,
                    away = fixture.AwayWinOdds,
                    over25 = fixture.Over25Odds,
                    btts = fixture.BttsYesOdds
                },
                odds_checked_at = fixture.OddsCheckedAtUtc
            });

            await predictionLedger.RecordAsync(
                fixture,
                prediction,
                analysis.RawPrediction ?? prediction,
                goalRateForecaster.ModelVersion ?? "dixon-coles",
                context,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Precompute] Could not record prediction for fixture {Id}", fixture.Id);
        }
    }

    private async Task UpsertSnapshotAsync(
        int fixtureId, string lang, MatchAnalysis mapped, WeightedPrediction? prediction, CancellationToken ct)
    {
        var row = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixtureId && a.Lang == lang, ct);

        if (row == null)
        {
            row = new FixtureAnalysis { FixtureId = fixtureId, Lang = lang };
            dbContext.FixtureAnalyses.Add(row);
        }

        row.SnapshotJson = AnalysisSnapshotSerializer.Serialize(mapped);
        row.UpdatedAt = DateTimeOffset.UtcNow;

        // Keep the math cache in sync with the snapshot (backtest reads these).
        if (prediction != null)
        {
            row.HomeProb = prediction.HomeProb;
            row.DrawProb = prediction.DrawProb;
            row.AwayProb = prediction.AwayProb;
            row.Over25Prob = prediction.Over25Prob;
            row.BttsProb = prediction.BTTSProb;
            row.Goals23Prob = prediction.TwoToThreeGoalsProb;
        }
    }
}
