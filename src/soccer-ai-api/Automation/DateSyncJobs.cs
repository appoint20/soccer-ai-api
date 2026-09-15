using System.Collections.Concurrent;
using SoccerAi.Application.Services.Sync;

namespace SoccerAi.Api.Automation;

public sealed record DateSyncJob(Guid Id, DateOnly Date, bool ForceAi, string State,
    DateTimeOffset StartedAtUtc, IReadOnlyList<DatePipelineStep> Steps,
    DateTimeOffset? FinishedAtUtc = null, string? Error = null);

/// <summary>
/// Request-independent jobs; progress is retained in memory for the newest 20
/// runs. Persisted fixture work survives restarts; job reports do not. The
/// shared gate does not lock a worker or a different API replica.
/// </summary>
public sealed class DateSyncJobs(DateSyncPipeline pipeline, ManualAutomationGate gate,
    IHostApplicationLifetime lifetime, ILogger<DateSyncJobs> logger)
{
    private readonly ConcurrentDictionary<Guid, DateSyncJob> _jobs = new();

    public DateSyncJob? Get(Guid id) => _jobs.GetValueOrDefault(id);
    public DateSyncJob? Current => _jobs.Values.FirstOrDefault(j => j.State == "running");

    public DateSyncJob? TryStart(DateOnly date, bool forceAi)
    {
        if (!gate.TryEnter()) return null;
        var job = new DateSyncJob(Guid.NewGuid(), date, forceAi, "running", DateTimeOffset.UtcNow,
            DateSyncPipeline.StepNames.Select(n => new DatePipelineStep(n)).ToArray());
        _jobs[job.Id] = job;
        foreach (var stale in _jobs.Values.Where(j => j.State != "running")
                     .OrderByDescending(j => j.StartedAtUtc).Skip(19))
            _jobs.TryRemove(stale.Id, out _);
        var ct = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            var state = "failed";
            string? error = null;
            try
            {
                await pipeline.RunAsync(date, forceAi, step =>
                    _jobs.AddOrUpdate(job.Id, job, (_, current) => current with
                    { Steps = current.Steps.Select(s => s.Name == step.Name ? step : s).ToArray() }), ct);
                state = "completed";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                state = "cancelled";
                error = "Cancelled by application shutdown. Completed fixture writes are preserved.";
            }
            catch (Exception ex)
            {
                // Exceptions can include connection strings or provider response
                // bodies. Keep the public report useful without echoing those.
                var failedStep = Get(job.Id)?.Steps.FirstOrDefault(s => s.State == "failed")?.Name;
                error = $"Date sync failed at {failedStep ?? "initialization"}. Check step reports and server logs; retry the date after resolving the error.";
                logger.LogError(ex, "[DateSyncJob] {JobId} for {Date} failed", job.Id, date);
            }
            finally
            {
                var outcome = Get(job.Id)! with
                {
                    State = state, Error = error, FinishedAtUtc = DateTimeOffset.UtcNow,
                    Steps = Get(job.Id)!.Steps.Select(s => s.State == "pending" ? s with { State = "skipped" } : s).ToArray()
                };
                gate.Exit();
                _jobs[job.Id] = outcome;
            }
        }, CancellationToken.None);
        return job;
    }
}
