namespace SoccerAi.Application.Interfaces;

/// <summary>
/// What a sample of old fixtures tells us about whether the API still prices
/// that window at all.
/// </summary>
public sealed record OddsBackfillProbe(int Sampled, int Priced)
{
    public double HitRate => Sampled > 0 ? (double)Priced / Sampled : 0;
}

public sealed record OddsBackfillResult(
    int MissingBefore,
    int Attempted,
    int Filled,
    int CallsUsed,
    string StopReason)
{
    public const string Completed = "completed";
    public const string MaxCallsReached = "max_calls_reached";
    public const string QuotaCritical = "quota_critical";
    public const string ProbeTooLow = "probe_hit_rate_too_low";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Fills in odds for fixtures the routine sync could never reach, because they
/// aged past its lookback window before a sync saw them.
///
/// Every price written here is a real quoted price. Nothing is estimated,
/// defaulted or substituted: a fixture with no price stays unpriced, because an
/// invented price produces invented expected value, and expected value is what
/// the entire product is sold on.
/// </summary>
public interface IOddsBackfillService
{
    /// <summary>
    /// Samples fixtures across the window to measure how many the API still
    /// prices. Prices found are kept — probing is cheap reconnaissance, not a
    /// throwaway.
    /// </summary>
    Task<OddsBackfillProbe> ProbeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int sampleSize, CancellationToken ct = default);

    /// <summary>
    /// Fetches missing odds newest-first, stopping on the call ceiling or when
    /// the daily quota gets tight.
    /// </summary>
    Task<OddsBackfillResult> BackfillAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxCalls, CancellationToken ct = default);
}
