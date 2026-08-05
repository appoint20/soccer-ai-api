namespace SoccerAi.Application.Interfaces;

/// <summary>Latest known quota state from API-Football response headers.</summary>
public sealed record ApiQuotaState(
    int? DailyLimit,
    int? DailyRemaining,
    int? MinuteLimit,
    int? MinuteRemaining,
    DateTimeOffset UpdatedAtUtc)
{
    public static ApiQuotaState Unknown { get; } = new(null, null, null, null, DateTimeOffset.MinValue);

    public double? DailyUsedShare =>
        DailyLimit is > 0 && DailyRemaining is not null
            ? 1.0 - (double)DailyRemaining.Value / DailyLimit.Value
            : null;
}

/// <summary>
/// Tracks API-Football quota from response headers so callers can slow down or
/// skip optional work BEFORE hitting 429.
/// </summary>
public interface IApiQuotaTracker
{
    ApiQuotaState Current { get; }

    /// <summary>Update from response headers (x-ratelimit-*).</summary>
    void Update(Func<string, string?> headerLookup);

    /// <summary>
    /// True when the daily quota is nearly exhausted — optional enrichment
    /// (stats, coaches, red cards) should be skipped to protect core syncing.
    /// </summary>
    bool IsDailyQuotaCritical { get; }

    /// <summary>Delay to apply before the next call (grows as the per-minute budget shrinks).</summary>
    TimeSpan SuggestedDelay { get; }
}
