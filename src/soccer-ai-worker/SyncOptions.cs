namespace SoccerAi.Worker;

/// <summary>
/// Sync worker configuration ("Sync" section). ALL times are UTC.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Daily run times in UTC, "HH:mm" (cron-style daily schedule).
    /// </summary>
    /// <remarks>
    /// Deliberately empty. The configuration binder <em>appends</em> bound array
    /// entries to whatever the property already holds instead of replacing them,
    /// so a default here is never overridden — it is concatenated with the
    /// configured value. That produced the duplicated
    /// "03:30, 15:30, 03:30, 15:30" schedule and made the times impossible to
    /// change from configuration. <see cref="SyncWorker.ParseSchedule"/> supplies
    /// the fallback when nothing is configured.
    /// </remarks>
    public string[] ScheduleUtc { get; set; } = [];

    /// <summary>
    /// On startup, sync immediately ONLY if the last successful sync is older
    /// than this many hours (persisted in the SyncStates table).
    /// </summary>
    public double StartupSyncThresholdHours { get; set; } = 20;

    /// <summary>How far ahead to forecast fixtures with the language models.</summary>
    public int ForecastDaysAhead { get; set; } = 3;

    /// <summary>Run the optional LLM narrative generation step.</summary>
    public bool GenerateAiNarratives { get; set; } = false;

    /// <summary>Precompute window around today, in days (past / future).</summary>
    public int RecomputeDaysBack { get; set; } = 3;
    public int RecomputeDaysAhead { get; set; } = 4;

    /// <summary>
    /// Interval for the T-schedule odds capture loop (first availability,
    /// T-24h, T-1h snapshots). 0 disables the loop.
    /// </summary>
    public int OddsCaptureIntervalMinutes { get; set; } = 30;
}
