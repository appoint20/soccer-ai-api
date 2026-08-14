using System.Text.Json.Serialization;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Automation;

/// <summary>Reads the sync agent's persisted state. No side effects.</summary>
public sealed class GetSyncStatusQuery : IRequest
{
    /// <summary>
    /// Age at which a sync is considered stale. Defaults to 26h: the schedule
    /// runs twice daily, so missing a full day is the first unambiguous signal
    /// something is wrong.
    /// </summary>
    public double StaleAfterHours { get; set; } = 26;
}

public sealed record GetSyncStatusResponse : IResponse
{
    /// <summary>
    /// <c>never_run</c>, <c>healthy</c>, <c>stale</c>, or <c>failing</c>.
    /// Read this rather than deriving a verdict from the timestamps.
    /// </summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("last_successful_sync_utc")] public required DateTimeOffset? LastSuccessfulSyncUtc { get; init; }
    [JsonPropertyName("last_run_started_utc")] public required DateTimeOffset? LastRunStartedUtc { get; init; }
    [JsonPropertyName("last_completed_step")] public required string? LastCompletedStep { get; init; }

    /// <summary>Error from the most recent run; null when the last run succeeded.</summary>
    [JsonPropertyName("last_error")] public required string? LastError { get; init; }

    [JsonPropertyName("hours_since_last_success")] public required double? HoursSinceLastSuccess { get; init; }
    [JsonPropertyName("is_stale")] public required bool IsStale { get; init; }

    /// <summary>
    /// Row counts, because a sync can report success while the tables stay
    /// empty and the counts are the only direct evidence of data.
    /// </summary>
    [JsonPropertyName("fixture_count")] public required int FixtureCount { get; init; }
    [JsonPropertyName("team_count")] public required int TeamCount { get; init; }
    [JsonPropertyName("analysis_count")] public required int AnalysisCount { get; init; }

    public static class Statuses
    {
        public const string NeverRun = "never_run";
        public const string Healthy = "healthy";
        public const string Stale = "stale";
        public const string Failing = "failing";
    }
}
