using System.Collections.Concurrent;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Automation;

public sealed record CombinedPredictionJob(Guid Id, int FixtureId, bool RefreshNarration, string State,
    DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc = null,
    CombinedPredictionRefreshResult? Result = null, string? Error = null);

public sealed class CombinedPredictionJobs(IServiceScopeFactory scopeFactory, ManualAutomationGate gate,
    IHostApplicationLifetime lifetime, ILogger<CombinedPredictionJobs> logger)
{
    private readonly ConcurrentDictionary<Guid, CombinedPredictionJob> _jobs = new();

    public CombinedPredictionJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public CombinedPredictionJob? TryStart(int fixtureId, bool refreshNarration)
    {
        if (!gate.TryEnter()) return null;
        var job = new CombinedPredictionJob(Guid.NewGuid(), fixtureId, refreshNarration, "running", DateTimeOffset.UtcNow);
        _jobs[job.Id] = job;
        foreach (var stale in _jobs.Values.Where(value => value.State != "running")
                     .OrderByDescending(value => value.StartedAtUtc).Skip(19))
            _jobs.TryRemove(stale.Id, out _);
        var ct = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            var outcome = job;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICombinedPredictionRefreshService>()
                    .RefreshAsync(fixtureId, refreshNarration, ct);
                outcome = job with
                {
                    State = result.Prediction.AllFourSourcesAvailable && (!refreshNarration || result.NarrationRefreshed)
                        ? "completed" : "completed_with_warnings", Result = result
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcome = job with { State = "cancelled", Error = "Cancelled by application shutdown. Completed database writes are retained." };
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "[CombinedPredictionJob] {JobId} failed for fixture {FixtureId}", job.Id, fixtureId);
                outcome = job with { State = "failed", Error = "Prediction refresh failed. Check fixture eligibility and server logs. Previously stored predictions are retained." };
            }
            finally
            {
                gate.Exit();
                _jobs[job.Id] = outcome with { FinishedAtUtc = DateTimeOffset.UtcNow };
            }
        }, CancellationToken.None);
        return job;
    }
}
