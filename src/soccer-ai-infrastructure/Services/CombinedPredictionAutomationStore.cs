using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.Infrastructure.Services;

public sealed class CombinedPredictionAutomationStore(ApplicationDbContext db) : ICombinedPredictionAutomationStore
{
    public async Task<bool> TryReserveAsync(CombinedPredictionAutomationAttempt attempt, int dailyLimit, CancellationToken ct)
    {
        if (dailyLimit <= 0) return false;
        var day = new DateTimeOffset(attempt.StartedAtUtc.UtcDateTime.Date, TimeSpan.Zero);
        var nextDay = day.AddDays(1);
        var abandonedBefore = attempt.StartedAtUtc.AddHours(-1);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.CombinedPredictionAutomationAttempts
                .Where(row => row.Status == "running" && row.StartedAtUtc <= abandonedBefore)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, "interrupted")
                    .SetProperty(row => row.FinishedAtUtc, attempt.StartedAtUtc)
                    .SetProperty(row => row.Error, "Worker stopped before recording an outcome; this window remains consumed."), ct);
            if (await db.CombinedPredictionAutomationAttempts.AnyAsync(row => row.Status == "running", ct)
                || await db.CombinedPredictionAutomationAttempts.CountAsync(row => row.StartedAtUtc >= day && row.StartedAtUtc < nextDay, ct) >= dailyLimit
                || await db.CombinedPredictionAutomationAttempts.AnyAsync(row => row.FixtureId == attempt.FixtureId
                    && row.KickoffUtc == attempt.KickoffUtc && row.Window == attempt.Window, ct)
                || !await db.Fixtures.AnyAsync(row => row.Id == attempt.FixtureId && row.Status == "NS"
                    && row.Date == attempt.KickoffUtc && row.Date > attempt.StartedAtUtc, ct))
            {
                await transaction.CommitAsync(ct);
                return false;
            }
            db.CombinedPredictionAutomationAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception exception) when (IsContention(exception)) { return false; }
        finally { db.Entry(attempt).State = EntityState.Detached; }
    }

    public async Task FinishAsync(Guid id, string status, Guid? snapshotId, string? error, DateTimeOffset finishedAt, CancellationToken ct)
    {
        await db.CombinedPredictionAutomationAttempts.Where(row => row.Id == id && row.Status == "running")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status)
                .SetProperty(row => row.SnapshotId, snapshotId).SetProperty(row => row.Error, error)
                .SetProperty(row => row.FinishedAtUtc, finishedAt), ct);
    }

    private static bool IsContention(Exception exception) => exception switch
    {
        PostgresException { SqlState: "40001" or "23505" or "40P01" } => true,
        SqliteException { SqliteErrorCode: 5 or 6 } => true,
        SqliteException { SqliteExtendedErrorCode: 1555 or 2067 } => true,
        _ => exception.InnerException is not null && IsContention(exception.InnerException)
    };
}
