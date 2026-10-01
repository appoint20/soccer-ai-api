using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Interfaces;

public interface ICombinedPredictionAutomation
{
    Task<CombinedPredictionAutomationReport> RunAsync(CancellationToken ct = default);
}

public sealed record CombinedPredictionAutomationReport(string Status, int Attempted = 0,
    int Completed = 0, int Partial = 0, int Failed = 0, string? Reason = null);

public interface ICombinedPredictionAutomationReadiness
{
    Task<string?> BlockingReasonAsync(CancellationToken ct);
}

public interface ICombinedPredictionAutomationStore
{
    Task<bool> TryReserveAsync(CombinedPredictionAutomationAttempt attempt, int dailyLimit, CancellationToken ct);
    Task FinishAsync(Guid id, string status, Guid? snapshotId, string? error, DateTimeOffset finishedAt, CancellationToken ct);
}
