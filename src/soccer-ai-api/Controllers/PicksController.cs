using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Picks;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

/// <summary>
/// The product surface: the day's stakeable tickets and confidence picks.
///
/// Every number here is produced by the statistical pipeline and selected by
/// the same code the backtest measures. No language model participates in
/// choosing or pricing a bet.
/// </summary>
[ApiController]
[Route("api/picks")]
[Authorize(Policy = "CombinedPolicy")]
public class PicksController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Tickets and confidence picks for a date (defaults to today, UTC).
    /// </summary>
    /// <param name="query">Optional date and language.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<ApiResponse<GetDailyPicksResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] GetDailyPicksQuery query, CancellationToken ct = default)
    {
        var response = await mediator
            .RequestAsync<GetDailyPicksQuery, GetDailyPicksResponse>(query, ct);

        return Ok(ApiResponse<GetDailyPicksResponse>.Ok(response));
    }
}
