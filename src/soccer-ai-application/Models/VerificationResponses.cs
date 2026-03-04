using Mediator.Net.Contracts;

namespace SoccerAi.Application.Models;

/// <summary>
/// Response for paginated fixture verification queries.
/// </summary>
public record FixtureVerificationResponse(
    int Count,
    List<FixtureSummaryDto> Data) : IResponse;

/// <summary>
/// Minimal fixture summary for verification purposes.
/// </summary>
public record FixtureSummaryDto(
    int Id,
    int ApiId,
    int LeagueId,
    int HomeTeamId,
    int AwayTeamId,
    int? HomeGoal,
    int? AwayGoal,
    double? HomeXg,
    double? AwayXg,
    DateTimeOffset CreatedAt);


/// <summary>
/// Response for paginated team verification queries.
/// </summary>
public record TeamVerificationResponse(
    int Count,
    List<TeamStandingDto> Data) : IResponse;

/// <summary>
/// Minimal team standing summary for verification.
/// </summary>
public record TeamStandingDto(
    int Id,
    int ApiId,
    string Name,
    int LeagueId,
    int Rank,
    int Points,
    string Form);


/// <summary>
/// Response for sync operations (fixtures or standings).
/// </summary>
public record SyncOperationResponse(
    int Created,
    int Updated,
    List<string> Errors) : IResponse;
