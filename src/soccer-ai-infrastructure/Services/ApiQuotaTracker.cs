using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Reads API-Football quota headers and turns them into throttling advice:
/// - x-ratelimit-requests-limit / -remaining  → daily budget
/// - X-RateLimit-Limit / X-RateLimit-Remaining → per-minute budget
///
/// Singleton: quota is per API key, shared by every caller in the process.
/// </summary>
public sealed class ApiQuotaTracker(ILogger<ApiQuotaTracker> logger) : IApiQuotaTracker
{
    /// <summary>Below this share of the daily budget, optional enrichment stops.</summary>
    private const double DailyCriticalRemainingShare = 0.10;

    /// <summary>Below this share of the per-minute budget, calls get spaced out.</summary>
    private const double MinuteThrottleRemainingShare = 0.20;

    private volatile ApiQuotaState _state = ApiQuotaState.Unknown;
    private int _lastLoggedDecile = -1;

    public ApiQuotaState Current => _state;

    public void Update(Func<string, string?> headerLookup)
    {
        static int? ParseInt(string? value) =>
            int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

        var dailyLimit = ParseInt(headerLookup("x-ratelimit-requests-limit"));
        var dailyRemaining = ParseInt(headerLookup("x-ratelimit-requests-remaining"));
        var minuteLimit = ParseInt(headerLookup("X-RateLimit-Limit"));
        var minuteRemaining = ParseInt(headerLookup("X-RateLimit-Remaining"));

        if (dailyLimit is null && dailyRemaining is null &&
            minuteLimit is null && minuteRemaining is null)
            return; // headers absent (e.g. cached/error response) — keep last known state

        _state = new ApiQuotaState(
            dailyLimit ?? _state.DailyLimit,
            dailyRemaining ?? _state.DailyRemaining,
            minuteLimit ?? _state.MinuteLimit,
            minuteRemaining ?? _state.MinuteRemaining,
            DateTimeOffset.UtcNow);

        LogOnDecileChange();
    }

    public bool IsDailyQuotaCritical
    {
        get
        {
            var s = _state;
            if (s.DailyLimit is not > 0 || s.DailyRemaining is null) return false;
            return (double)s.DailyRemaining.Value / s.DailyLimit.Value <= DailyCriticalRemainingShare;
        }
    }

    public TimeSpan SuggestedDelay
    {
        get
        {
            var s = _state;
            if (s.MinuteLimit is not > 0 || s.MinuteRemaining is null)
                return TimeSpan.FromMilliseconds(100); // unknown → conservative default

            var remainingShare = (double)s.MinuteRemaining.Value / s.MinuteLimit.Value;

            return remainingShare switch
            {
                <= 0.05 => TimeSpan.FromSeconds(10), // almost out: wait for the window to reset
                <= MinuteThrottleRemainingShare => TimeSpan.FromSeconds(2),
                <= 0.50 => TimeSpan.FromMilliseconds(500),
                _ => TimeSpan.FromMilliseconds(100)
            };
        }
    }

    private void LogOnDecileChange()
    {
        var used = _state.DailyUsedShare;
        if (used is null) return;

        var decile = (int)(used.Value * 10);
        if (decile == _lastLoggedDecile) return;
        _lastLoggedDecile = decile;

        var level = IsDailyQuotaCritical ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level,
            "[ApiQuota] Daily {Remaining}/{Limit} remaining ({Used:P0} used); per-minute {MinRemaining}/{MinLimit}",
            _state.DailyRemaining, _state.DailyLimit, used, _state.MinuteRemaining, _state.MinuteLimit);
    }
}
