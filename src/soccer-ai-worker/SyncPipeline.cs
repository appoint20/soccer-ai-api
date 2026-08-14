using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Worker;

/// <summary>
/// The sync pipeline: standings → fixtures/results+odds → recompute analysis
/// → (optional) LLM narratives.
///
/// - Idempotent: every step upserts; running twice is safe.
/// - Resumable: the last completed step is persisted; an interrupted run
///   resumes from the following step.
/// - Rate-limit aware: an API-Football rate-limit abort stops the run cleanly;
///   the next scheduled run resumes.
/// - Per-step logging with timings.
/// </summary>
public sealed class SyncPipeline(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    IApiCallTracker apiCalls,
    ILogger<SyncPipeline> logger)
{
    private static readonly string[] StepOrder =
    [
        Steps.Standings,
        Steps.FixturesAndOdds,
        Steps.RecomputeAnalysis,
        Steps.SettlePicks,
        Steps.PublishPicks,
        Steps.AiNarratives
    ];

    public static class Steps
    {
        public const string Standings = "standings";
        public const string FixturesAndOdds = "fixtures_odds";
        public const string RecomputeAnalysis = "recompute_analysis";

        /// <summary>Settle yesterday's published tickets against fresh results.</summary>
        public const string SettlePicks = "settle_picks";

        /// <summary>Freeze today's board into the ledger at the prices shown.</summary>
        public const string PublishPicks = "publish_picks";

        public const string AiNarratives = "ai_narratives";
    }

    private static int CurrentSeason(DateTimeOffset nowUtc) =>
        nowUtc.Month >= 7 ? nowUtc.Year : nowUtc.Year - 1;

    private static bool IsCredentialFailure(ExternalApiException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden;

    /// <summary>Runs the pipeline. Returns true when it completed fully.</summary>
    public async Task<bool> RunAsync(bool resume, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var season = CurrentSeason(nowUtc);
        var opt = options.Value;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var state = await GetOrCreateStateAsync(db, ct);

        // Resume support: skip steps completed in an interrupted previous run.
        var startIndex = 0;
        var previousRunIncomplete = state.LastCompletedStep != null &&
            (state.LastSuccessfulSyncUtc == null || state.LastSuccessfulSyncUtc < state.LastRunStartedUtc);
        if (resume && previousRunIncomplete)
        {
            startIndex = Array.IndexOf(StepOrder, state.LastCompletedStep) + 1;
            if (startIndex > 0)
                logger.LogInformation("[Sync] Resuming interrupted run after step '{Step}'", state.LastCompletedStep);
        }

        if (startIndex == 0)
        {
            state.LastRunStartedUtc = nowUtc;
            state.LastCompletedStep = null;
        }
        state.LastError = null;
        await db.SaveChangesAsync(ct);

        apiCalls.Reset();

        logger.LogInformation("[Sync] Run started (season {Season}, from step {Index}/{Total})",
            season, startIndex + 1, StepOrder.Length);

        try
        {
            for (var i = startIndex; i < StepOrder.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = StepOrder[i];

                if (step == Steps.AiNarratives && !opt.GenerateAiNarratives)
                {
                    logger.LogInformation("[Sync] Step '{Step}' skipped (disabled by config)", step);
                    await MarkStepDoneAsync(db, state, step, ct);
                    continue;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                logger.LogInformation("[Sync] Step '{Step}' starting...", step);

                await ExecuteStepAsync(scope.ServiceProvider, step, season, opt, ct);

                sw.Stop();
                logger.LogInformation("[Sync] Step '{Step}' completed in {Elapsed:F1}s", step, sw.Elapsed.TotalSeconds);
                await MarkStepDoneAsync(db, state, step, ct);
            }

            // Every step "completed", but completion is not the same as having
            // fetched anything. When every upstream call was rejected, each step
            // ran to the end over an empty result set — recording that as a
            // successful sync clears LastError, hides the outage, and makes the
            // next startup skip its catch-up run. Fail the run instead.
            var calls = apiCalls.Current;
            if (calls.AllFailed)
            {
                state.LastError = $"All {calls.Attempted} upstream API calls failed: {calls.LastError}";
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogError(
                    "[Sync] Run produced no data — all {Attempted} API-Football calls failed ({Error}). "
                    + "Not marking the sync successful.",
                    calls.Attempted, calls.LastError);
                return false;
            }

            state.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            state.LastError = null;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "[Sync] Run completed successfully ({Succeeded}/{Attempted} API calls succeeded)",
                calls.Succeeded, calls.Attempted);
            return true;
        }
        catch (ExternalApiException ex) when (IsCredentialFailure(ex))
        {
            // A rejected key will not fix itself on the next run, so it is logged
            // as an error rather than filed under the transient-limit path.
            state.LastError = $"Credential rejected: {ex.Message}";
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex,
                "[Sync] Aborted: {Service} rejected the API key. The sync cannot run until the "
                + "credential is corrected — this will not resolve on the next scheduled run.",
                ex.ServiceName);
            return false;
        }
        catch (ExternalApiException ex)
        {
            // Rate limit / quota: stop cleanly, resume at the next scheduled run.
            state.LastError = $"API limit: {ex.Message}";
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(ex, "[Sync] Aborted by external API limit — will resume next run");
            return false;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[Sync] Run cancelled (shutdown)");
            throw;
        }
        catch (Exception ex)
        {
            state.LastError = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "[Sync] Run failed at step after '{Step}'", state.LastCompletedStep ?? "<none>");
            return false;
        }
    }

    private async Task ExecuteStepAsync(
        IServiceProvider services, string step, int season, SyncOptions opt, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        switch (step)
        {
            case Steps.Standings:
                await services.GetRequiredService<ITeamSyncService>()
                    .SyncAllLeaguesAsync(season, ct);
                break;

            case Steps.FixturesAndOdds:
                // Fixture sync includes results and the odds snapshot for
                // fixtures inside the odds window.
                await services.GetRequiredService<IFixtureSyncService>()
                    .SyncAllLeaguesAsync(season, ct);
                break;

            case Steps.RecomputeAnalysis:
                await services.GetRequiredService<IAnalysisPrecomputeService>()
                    .RecomputeWindowAsync(
                        nowUtc.Date.AddDays(-opt.RecomputeDaysBack),
                        nowUtc.Date.AddDays(opt.RecomputeDaysAhead), ct);
                break;

            // Settle before publishing: results that just arrived belong to
            // yesterday's board, and settling first keeps the two concerns from
            // interleaving on the same run.
            case Steps.SettlePicks:
                await services.GetRequiredService<IPickLedger>().SettleAsync(ct);
                break;

            // Freeze today's board at the prices currently shown. The ledger is
            // idempotent, so a second run of the day adds only genuinely new
            // tickets and never rewrites a recorded price.
            case Steps.PublishPicks:
                await PublishPicksAsync(services, nowUtc, opt, ct);
                break;

            case Steps.AiNarratives:
                await services.GetRequiredService<IAiSyncService>()
                    .SyncUpcomingFixturesAsync(nowUtc.UtcDateTime, force: false, ct);
                break;

            default:
                throw new InvalidOperationException($"Unknown sync step: {step}");
        }
    }

    /// <summary>
    /// Records the boards inside the recompute-ahead window, so a ticket for a
    /// fixture two days out is captured at today's price rather than whatever it
    /// has drifted to by kickoff.
    /// </summary>
    private static async Task PublishPicksAsync(
        IServiceProvider services, DateTimeOffset nowUtc, SyncOptions opt, CancellationToken ct)
    {
        var pickService = services.GetRequiredService<IDailyPickService>();
        var ledger = services.GetRequiredService<IPickLedger>();

        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);

        for (var offset = 0; offset <= opt.RecomputeDaysAhead; offset++)
        {
            ct.ThrowIfCancellationRequested();

            var board = await pickService.GetBoardAsync(today.AddDays(offset), "en", ct);
            await ledger.RecordAsync(board, ct);
        }
    }

    private static async Task MarkStepDoneAsync(
        IApplicationDbContext db, SyncState state, string step, CancellationToken ct)
    {
        state.LastCompletedStep = step;
        await db.SaveChangesAsync(ct);
    }

    public static async Task<SyncState> GetOrCreateStateAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var state = await db.SyncStates.FirstOrDefaultAsync(s => s.Id == SyncState.SingletonId, ct);
        if (state == null)
        {
            state = new SyncState { Id = SyncState.SingletonId };
            db.SyncStates.Add(state);
            await db.SaveChangesAsync(ct);
        }
        return state;
    }
}
