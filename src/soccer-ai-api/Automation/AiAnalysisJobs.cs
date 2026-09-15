using System.Collections.Concurrent;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Automation;

/// <summary>A manual AI analysis run for one date, as a poller sees it.</summary>
public sealed record AiAnalysisJob(
    Guid Id,
    DateOnly Date,
    bool Force,
    string State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc = null,
    AiSyncReport? Report = null,
    string? Error = null)
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithFailures = "completed_with_failures";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Runs manual AI analysis jobs in the background and keeps their status in
/// memory for polling.
/// </summary>
/// <remarks>
/// One background automation job at a time in this API process, sharing the
/// gate with full date sync and sync-pipeline. Narratives are written one fixture per model request, so
/// a second concurrent job would pay twice for the same fixtures and race the
/// same rows. The gate is per process: it cannot see the worker's scheduled
/// narrative step, which can still overlap — the per-fixture "already has text"
/// check stops that producing duplicates, but not paying twice.
///
/// Status lives in memory, so a restart forgets it and only recent jobs are
/// kept. Narratives are saved as each fixture completes, so a restart loses the
/// report, never finished work.
/// </remarks>
public sealed class AiAnalysisJobs(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<AiAnalysisJobs> logger,
    ManualAutomationGate gate)
{
    private const int RetainedJobs = 20;

    private readonly ConcurrentDictionary<Guid, AiAnalysisJob> _jobs = new();

    /// <summary>Starts a job, or returns null when one is already running.</summary>
    public AiAnalysisJob? TryStart(DateOnly date, bool force)
    {
        if (!gate.TryEnter()) return null;

        var job = new AiAnalysisJob(Guid.NewGuid(), date, force, AiAnalysisJob.Running, DateTimeOffset.UtcNow);
        _jobs[job.Id] = job;
        Trim();

        // ApplicationStopping, not a request token: the job outlives the
        // response, and only a host shutdown should cancel it.
        var ct = lifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            var outcome = job with { State = AiAnalysisJob.Failed, Error = "The job ended without a result." };
            try
            {
                // Its own scope: IAiSyncService and its DbContext are scoped, and
                // the request's scope is disposed as soon as the 202 is sent.
                await using var scope = scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<IAiSyncService>();

                var report = await sync.SyncDateAsync(date, force, ct);
                outcome = job with
                {
                    State = report.Failed == 0 ? AiAnalysisJob.Completed : AiAnalysisJob.CompletedWithFailures,
                    Report = report,
                    Error = null,
                };
                logger.LogInformation(
                    "[AiAnalysisJob] {JobId} for {Date:yyyy-MM-dd} finished: {Generated} generated, {Failed} failed",
                    job.Id, date, report.Generated, report.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcome = job with { State = AiAnalysisJob.Cancelled, Error = "Cancelled by application shutdown." };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AiAnalysisJob] {JobId} for {Date:yyyy-MM-dd} failed", job.Id, date);
                outcome = job with { State = AiAnalysisJob.Failed, Error = ex.Message };
            }
            finally
            {
                // Release before publishing the outcome: a poller that sees the
                // job finish must be able to start the next one at once, not
                // race the release and get a spurious 409.
                gate.Exit();
                Finish(outcome);
            }
        }, CancellationToken.None);

        return job;
    }

    public AiAnalysisJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    /// <summary>The job currently holding the gate, if any.</summary>
    public AiAnalysisJob? Current => _jobs.Values.FirstOrDefault(j => j.State == AiAnalysisJob.Running);

    private void Finish(AiAnalysisJob job) => _jobs[job.Id] = job with { FinishedAtUtc = DateTimeOffset.UtcNow };

    /// <summary>Keeps the newest finished jobs; a running job is never evicted.</summary>
    private void Trim()
    {
        foreach (var stale in _jobs.Values
                     .Where(j => j.State != AiAnalysisJob.Running)
                     .OrderByDescending(j => j.StartedAtUtc)
                     .Skip(RetainedJobs - 1))
            _jobs.TryRemove(stale.Id, out _);
    }
}
