using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Verification;

/// <summary>
/// Handles fixture verification queries.
/// Eliminates direct database access from controller layer.
/// </summary>
public class GetFixturesVerificationHandler(
    FixtureQueryHelper queryHelper,
    ILogger<GetFixturesVerificationHandler> logger)
    : IRequestHandler<GetFixturesVerificationQuery, FixtureVerificationResponse>
{
    public async Task<FixtureVerificationResponse> Handle(
        IReceiveContext<GetFixturesVerificationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Fetching fixtures: limit={Limit}, offset={Offset}", query.Limit, query.Offset);

        var (fixtures, total) = await queryHelper.GetPaginatedFixturesAsync(
            query.Limit,
            query.Offset,
            cancellationToken);

        var data = fixtures.Select(f => new FixtureSummaryDto(
            f.Id,
            f.ApiId,
            f.LeagueId,
            f.HomeTeamId,
            f.AwayTeamId,
            f.HomeGoal,
            f.AwayGoal,
            f.HomeXg,
            f.AwayXg,
            f.CreatedAt
        )).ToList();

        return new FixtureVerificationResponse(total, data);
    }
}

/// <summary>
/// Handles team verification queries.
/// Eliminates direct database access from controller layer.
/// </summary>
public class GetTeamsVerificationHandler(
    FixtureQueryHelper queryHelper,
    ILogger<GetTeamsVerificationHandler> logger)
    : IRequestHandler<GetTeamsVerificationQuery, TeamVerificationResponse>
{
    public async Task<TeamVerificationResponse> Handle(
        IReceiveContext<GetTeamsVerificationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Fetching teams: limit={Limit}, offset={Offset}, leagueId={LeagueId}",
            query.Limit, query.Offset, query.LeagueId);

        var (teams, total) = await queryHelper.GetPaginatedTeamsAsync(
            query.Limit,
            query.Offset,
            query.LeagueId,
            cancellationToken);

        var data = teams.Select(t => new TeamStandingDto(
            t.Id,
            t.ApiId,
            t.Name,
            t.LeagueId,
            t.Rank,
            t.Points,
            t.Form
        )).ToList();

        return new TeamVerificationResponse(total, data);
    }
}

/// <summary>
/// Handles fixture synchronization commands.
/// Delegates to sync service and formats response.
/// </summary>
public class SyncLeagueFixturesHandler(
    IFixtureSyncService syncService,
    ILogger<SyncLeagueFixturesHandler> logger)
    : ICommandHandler<SyncLeagueFixturesCommand, SyncOperationResponse>
{
    public async Task<SyncOperationResponse> Handle(
        IReceiveContext<SyncLeagueFixturesCommand> context,
        CancellationToken cancellationToken)
    {
        var command = context.Message;
        logger.LogInformation("Syncing fixtures for league {LeagueId}, season {Season}",
            command.LeagueId, command.Season);

        var result = await syncService.SyncLeagueFixturesAsync(
            command.LeagueId,
            command.Season,
            cancellationToken);

        return new SyncOperationResponse(
            result.Created,
            result.Updated,
            result.ErrorMessages);
    }
}

/// <summary>
/// Handles standings synchronization commands.
/// Delegates to sync service and formats response.
/// </summary>
public class SyncLeagueStandingsHandler(
    ITeamSyncService syncService,
    ILogger<SyncLeagueStandingsHandler> logger)
    : ICommandHandler<SyncLeagueStandingsCommand, SyncOperationResponse>
{
    public async Task<SyncOperationResponse> Handle(
        IReceiveContext<SyncLeagueStandingsCommand> context,
        CancellationToken cancellationToken)
    {
        var command = context.Message;
        logger.LogInformation("Syncing standings for league {LeagueId}, season {Season}",
            command.LeagueId, command.Season);

        var result = await syncService.SyncLeagueStandingsAsync(
            command.LeagueId,
            command.Season,
            cancellationToken);

        return new SyncOperationResponse(
            result.Created,
            result.Updated,
            result.ErrorMessages);
    }
}
