namespace SoccerAi.Application.Entities;

/// <summary>
/// Persisted sync agent state (single row, Id = 1). Lets the worker decide on
/// startup whether a sync is due (last success &gt; 20h ago) and resume an
/// interrupted run from the step after the last completed one.
/// </summary>
public class SyncState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>UTC timestamp of the last fully successful pipeline run.</summary>
    public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

    /// <summary>UTC timestamp when the most recent run started.</summary>
    public DateTimeOffset? LastRunStartedUtc { get; set; }

    /// <summary>Last pipeline step that completed in the most recent run.</summary>
    public string? LastCompletedStep { get; set; }

    /// <summary>Error message of the most recent failed run, null when healthy.</summary>
    public string? LastError { get; set; }
}
