namespace SoccerAi.Application.Services.Sync;

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
    /// change from configuration. The sync worker's schedule parser supplies the
    /// fallback when nothing is configured.
    /// </remarks>
    public string[] ScheduleUtc { get; set; } = [];

    /// <summary>
    /// Generates the schedule at a fixed cadence instead of listing every slot.
    /// 0 (the default) uses <see cref="ScheduleUtc"/> as written.
    /// </summary>
    /// <remarks>
    /// A cadence is one number; the equivalent explicit grid is 24 configuration
    /// entries, and on Render 24 separate indexed environment variables. That is
    /// not merely verbose — the binder appends array entries rather than
    /// replacing them, so a long indexed list is exactly the shape that produced
    /// the duplicated schedule this class already warns about. One scalar cannot
    /// half-apply.
    ///
    /// The generated slots are still absolute UTC times, not "every N minutes
    /// since the process started", so a restart or a slow run cannot shift the
    /// grid or let two runs overlap.
    ///
    /// An interval that does not divide 1440 evenly leaves one short or long gap
    /// where the last slot of the day wraps to the first of the next; every slot
    /// before that is exact.
    /// </remarks>
    public int IntervalMinutes { get; set; }

    /// <summary>
    /// Minutes past the hour the generated grid is anchored to. Matches the
    /// hand-written schedule it replaces, which deliberately avoids the top of
    /// the hour — the busiest moment for the upstream provider.
    /// </summary>
    public int IntervalAnchorMinute { get; set; } = 20;

    /// <summary>
    /// On startup, sync immediately ONLY if the last successful sync is older
    /// than this many hours (persisted in the SyncStates table).
    /// </summary>
    public double StartupSyncThresholdHours { get; set; } = 3;

    /// <summary>How far ahead to forecast fixtures with the language models.</summary>
    public int ForecastDaysAhead { get; set; } = 3;

    /// <summary>Run the optional LLM narrative generation step.</summary>
    public bool GenerateAiNarratives { get; set; } = true;

    /// <summary>
    /// How far ahead the narrative step reaches. Separate from
    /// <see cref="RecomputeDaysAhead"/>: the snapshot recompute is cheap local
    /// maths, while every day added here is a paid model call per fixture.
    /// </summary>
    public int AiNarrativeDaysAhead { get; set; } = 5;

    /// <summary>Precompute window around today, in days (past / future).</summary>
    public int RecomputeDaysBack { get; set; } = 3;
    public int RecomputeDaysAhead { get; set; } = 4;

    /// <summary>
    /// How often the odds loop wakes. 0 disables the loop. This is only the
    /// tick rate — <see cref="OddsRefreshIntervalHours"/> decides whether a
    /// given fixture is actually re-priced on that tick.
    /// </summary>
    public int OddsCaptureIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// How far ahead of kickoff a fixture becomes eligible for odds capture.
    /// </summary>
    public int OddsCaptureHorizonHours { get; set; } = 72;

    /// <summary>
    /// Minimum age of a fixture's newest quote before it is re-priced.
    /// </summary>
    /// <remarks>
    /// The previous loop captured at three milestones — first availability,
    /// T-24h and T-1h — and in practice only the first ever fired: of 9,752
    /// fixtures with stored quotes, 9,751 carry exactly ONE capture and no
    /// bookmaker/market series has more than one. So every published price was
    /// whatever the book offered one to three days out, and never moved again.
    ///
    /// That matters because the value gate reads the fixture's odds columns. A
    /// price of 1.75 captured two days early clears the 1.70 floor and gets
    /// published as a value bet; by kickoff the same market is 1.60 and the
    /// edge never existed. Re-pricing on a fixed cadence is what makes the gate
    /// reflect a price someone could still take.
    /// </remarks>
    public double OddsRefreshIntervalHours { get; set; } = 3;

    /// <summary>
    /// Inside this many hours of kickoff, re-price on every tick instead of
    /// waiting for <see cref="OddsRefreshIntervalHours"/>.
    /// </summary>
    /// <remarks>
    /// Lines move fastest late — team news lands, and that is exactly when a
    /// stale price is most likely to have crossed the minimum-odds floor.
    /// </remarks>
    public double OddsFinalApproachHours { get; set; } = 6;

    /// <summary>
    /// How far ahead of kickoff to start asking for reported absences. 0 disables.
    /// </summary>
    /// <remarks>
    /// Measured against the live API: /injuries returns nothing 2-3 days out and
    /// is populated from roughly 24h before kickoff, so a wider window buys
    /// empty responses. 30h leaves margin for the provider publishing early
    /// without wasting a request per fixture per cycle on silence.
    ///
    /// Cost is modest — one request per fixture per capture, on the same loop
    /// that already walks upcoming fixtures — against a 7,500/day budget
    /// currently running at well under 1%.
    /// </remarks>
    public double InjuryCaptureHorizonHours { get; set; } = 30;

    /// <summary>Minimum age of a fixture's newest injury capture before refetching.</summary>
    public double InjuryRefreshIntervalHours { get; set; } = 6;
}
