using System.Text.Json.Serialization;
using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Leagues;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

/// <summary>A tracked competition.</summary>
public sealed record LeagueDto
{
    [JsonPropertyName("id")] public required int Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
}

[ApiController]
[Route("api/leagues")]
[Authorize(Policy = "CombinedPolicy")]
public class LeagueController(IMediator mediator) : ControllerBase
{
    private static readonly LeagueDto[] Leagues =
    [
        new() { Id = 39, Name = "Premier League" },
        new() { Id = 40, Name = "Championship" },
        new() { Id = 41, Name = "League One" },
        new() { Id = 42, Name = "League Two" },
        new() { Id = 78, Name = "Bundesliga" },
        new() { Id = 79, Name = "2. Bundesliga" },
        new() { Id = 80, Name = "3. Liga" },
        new() { Id = 135, Name = "Serie A" },
        new() { Id = 136, Name = "Serie B" },
        new() { Id = 140, Name = "La Liga" },
        new() { Id = 141, Name = "La Liga 2" },
        new() { Id = 61, Name = "Ligue 1" },
        new() { Id = 62, Name = "Ligue 2" },
        new() { Id = 43, Name = "English National League" },
        new() { Id = 2, Name = "UEFA Champions League" },
        new() { Id = 3, Name = "UEFA Europa League" }
    ];

    /// <summary>
    /// The leagues this deployment tracks.
    /// </summary>
    /// <remarks>
    /// Paged for envelope consistency rather than for size — the list is a fixed
    /// sixteen entries, so the default window returns all of them in one call.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResponse<LeagueDto>>>(StatusCodes.Status200OK)]
    public IActionResult GetLeagues([FromQuery] PageQuery query)
    {
        var page = PagedResponse<LeagueDto>.FromSource(
            Leagues, query.ResolveLimit(), query.ResolveOffset());

        return Ok(ApiResponse<PagedResponse<LeagueDto>>.Ok(page));
    }

    /// <summary>
    /// Audit endpoint to check persistence status for a specific league.
    /// </summary>
    [HttpGet("{leagueId:int}/status")]
    [ProducesResponseType<ApiResponse<GetLeagueStatusResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(int leagueId, CancellationToken ct)
    {
        var query = new GetLeagueStatusQuery { LeagueId = leagueId };
        var response = await mediator.RequestAsync<GetLeagueStatusQuery, GetLeagueStatusResponse>(query, ct);
        return Ok(ApiResponse<GetLeagueStatusResponse>.Ok(response));
    }
}
