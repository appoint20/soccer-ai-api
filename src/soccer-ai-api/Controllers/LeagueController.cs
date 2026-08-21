using System.Text.Json.Serialization;
using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Leagues;
using SoccerAi.Application.Interfaces;
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
public class LeagueController(IMediator mediator, ILeagueTierService leagueTiers) : ControllerBase
{
    /// <summary>
    /// Display names for every league id the tier configuration can contain.
    /// </summary>
    /// <remarks>
    /// Names only. Which leagues are actually tracked is decided by
    /// <see cref="ILeagueTierService.GetSyncLeagueIds"/>, so this table cannot
    /// drift into advertising something the sync never fetches.
    /// </remarks>
    private static readonly Dictionary<int, string> LeagueNames = new()
    {
        [39] = "Premier League",
        [40] = "Championship",
        [41] = "League One",
        [42] = "League Two",
        [46] = "National League",
        [5] = "National League",
        [78] = "Bundesliga",
        [79] = "2. Bundesliga",
        [80] = "3. Liga",
        [140] = "La Liga",
        [141] = "La Liga 2",
        [135] = "Serie A",
        [136] = "Serie B",
        [61] = "Ligue 1",
        [62] = "Ligue 2",
        [2] = "UEFA Champions League",
        [3] = "UEFA Europa League",
        [848] = "UEFA Europa Conference League",
    };

    /// <summary>
    /// Ids that name a competition already listed under another id. 5 is a
    /// legacy placeholder for the National League, which syncs as 46; listing
    /// both would show one competition twice.
    /// </summary>
    private static readonly HashSet<int> LegacyAliasIds = [5];

    /// <summary>
    /// The leagues this deployment tracks.
    /// </summary>
    /// <remarks>
    /// Derived from the sync scope, not from a hand-maintained list. The two
    /// had drifted apart: this endpoint advertised the National League under id
    /// 43 while the sync fetched it as 46, and offered the Champions and Europa
    /// Leagues, which are Tier 2 and not synced at all unless
    /// <c>LeagueTiers:IncludeTier2</c> is on. Both mistakes are invisible from
    /// the client — a league is simply listed and then never has fixtures.
    ///
    /// Paged for envelope consistency rather than for size: the default window
    /// of 50 comfortably returns every entry in one call.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResponse<LeagueDto>>>(StatusCodes.Status200OK)]
    public IActionResult GetLeagues([FromQuery] PageQuery query)
    {
        var leagues = leagueTiers.GetSyncLeagueIds()
            .Where(id => !LegacyAliasIds.Contains(id))
            .Select(id => new LeagueDto
            {
                Id = id,
                Name = LeagueNames.GetValueOrDefault(id, $"League {id}")
            })
            .ToList();

        var page = PagedResponse<LeagueDto>.FromSource(
            leagues, query.ResolveLimit(), query.ResolveOffset());

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
