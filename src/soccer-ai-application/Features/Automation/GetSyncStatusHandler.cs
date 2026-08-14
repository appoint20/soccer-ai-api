using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Automation;

/// <summary>
/// Reports whether the sync agent is actually working.
///
/// This exists because /api/automation/health answers a different question: it
/// returns a constant and never touches the database, so it reads "healthy"
/// while the worker has been failing for a week.
/// </summary>
public sealed class GetSyncStatusHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSyncStatusQuery, GetSyncStatusResponse>
{
    public async Task<GetSyncStatusResponse> Handle(
        IReceiveContext<GetSyncStatusQuery> context,
        CancellationToken cancellationToken)
    {
        var staleAfter = TimeSpan.FromHours(context.Message.StaleAfterHours);

        var state = await dbContext.SyncStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SyncState.SingletonId, cancellationToken);

        var fixtures = await dbContext.Fixtures.CountAsync(cancellationToken);
        var teams = await dbContext.Teams.CountAsync(cancellationToken);
        var analyses = await dbContext.FixtureAnalyses.CountAsync(cancellationToken);

        var lastSuccess = state?.LastSuccessfulSyncUtc;
        var age = lastSuccess.HasValue ? DateTimeOffset.UtcNow - lastSuccess.Value : (TimeSpan?)null;
        var isStale = age is null || age > staleAfter;

        return new GetSyncStatusResponse
        {
            Status = Classify(state, lastSuccess, isStale),
            LastSuccessfulSyncUtc = lastSuccess,
            LastRunStartedUtc = state?.LastRunStartedUtc,
            LastCompletedStep = state?.LastCompletedStep,
            LastError = state?.LastError,
            HoursSinceLastSuccess = age.HasValue ? Math.Round(age.Value.TotalHours, 2) : null,
            IsStale = isStale,
            FixtureCount = fixtures,
            TeamCount = teams,
            AnalysisCount = analyses
        };
    }

    /// <summary>
    /// An unresolved error outranks a recent success: the last run failed, and
    /// yesterday's success does not make the agent healthy today.
    /// </summary>
    private static string Classify(SyncState? state, DateTimeOffset? lastSuccess, bool isStale)
    {
        if (state is null || lastSuccess is null)
            return GetSyncStatusResponse.Statuses.NeverRun;

        if (!string.IsNullOrWhiteSpace(state.LastError))
            return GetSyncStatusResponse.Statuses.Failing;

        return isStale
            ? GetSyncStatusResponse.Statuses.Stale
            : GetSyncStatusResponse.Statuses.Healthy;
    }
}
