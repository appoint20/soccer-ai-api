using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SoccerAi.Application.Features.Verification;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

/// <summary>
/// Verification endpoint for managing fixture and team synchronization.
/// Uses CQRS pattern to maintain clean architecture separation.
/// </summary>
[ApiController]
[Route("api/verify")]
[Authorize(Policy = "CombinedPolicy")]
public class VerificationController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Retrieves paginated fixture list for verification purposes.
    /// </summary>
    /// <param name="limit">Maximum number of fixtures to return (default: 50)</param>
    /// <param name="offset">Number of fixtures to skip (default: 0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated fixture summary list</returns>
    [HttpGet("fixtures")]
    [ProducesResponseType<FixtureVerificationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFixtures(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = new GetFixturesVerificationQuery(limit, offset);
        var response = await mediator.RequestAsync<GetFixturesVerificationQuery, FixtureVerificationResponse>(query, ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieves paginated team list for verification purposes.
    /// </summary>
    /// <param name="limit">Maximum number of teams to return (default: 50)</param>
    /// <param name="offset">Number of teams to skip (default: 0)</param>
    /// <param name="leagueId">Optional league filter</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated team standing list</returns>
    [HttpGet("teams")]
    [ProducesResponseType<TeamVerificationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeams(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] int? leagueId = null,
        CancellationToken ct = default)
    {
        var query = new GetTeamsVerificationQuery(limit, offset, leagueId);
        var response = await mediator.RequestAsync<GetTeamsVerificationQuery, TeamVerificationResponse>(query, ct);
        return Ok(response);
    }

    /// <summary>
    /// Synchronizes fixtures for a specific league and season.
    /// </summary>
    /// <param name="leagueId">The league ID to sync</param>
    /// <param name="season">The season year to sync</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Sync operation summary</returns>
    [HttpPost("sync/fixtures/{leagueId}")]
    [ProducesResponseType<SyncOperationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncFixtures(
        int leagueId,
        [FromQuery] int season,
        CancellationToken ct = default)
    {
        var command = new SyncLeagueFixturesCommand(leagueId, season);
        var response = await mediator.SendAsync<SyncLeagueFixturesCommand, SyncOperationResponse>(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Synchronizes standings for a specific league and season.
    /// </summary>
    /// <param name="leagueId">The league ID to sync</param>
    /// <param name="season">The season year to sync</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Sync operation summary</returns>
    [HttpPost("sync/standings/{leagueId}")]
    [ProducesResponseType<SyncOperationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncStandings(
        int leagueId,
        [FromQuery] int season,
        CancellationToken ct = default)
    {
        var command = new SyncLeagueStandingsCommand(leagueId, season);
        var response = await mediator.SendAsync<SyncLeagueStandingsCommand, SyncOperationResponse>(command, ct);
        return Ok(response);
    }
}
