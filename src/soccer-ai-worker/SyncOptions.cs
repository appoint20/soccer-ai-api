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
    public string[] ScheduleUtc { get; set; } = ["03:30", "15:30"];

    /// <summary>
    /// On startup, sync immediately ONLY if the last successful sync is older
    /// than this many hours (persisted in the SyncStates table).
    /// </summary>
    public double StartupSyncThresholdHours { get; set; } = 20;

    /// <summary>Run the optional LLM narrative generation step.</summary>
    public bool GenerateAiNarratives { get; set; } = false;

    /// <summary>Precompute window around today, in days (past / future).</summary>
    public int RecomputeDaysBack { get; set; } = 3;
    public int RecomputeDaysAhead { get; set; } = 4;
}
