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

    /// <summary>
    /// What published tickets actually returned. Unlike the backtest, these are
    /// live results at the prices customers were shown.
    /// </summary>
    /// <param name="query">Optional date range; defaults to the last 90 days.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("performance")]
    [ProducesResponseType<ApiResponse<GetPickPerformanceResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerformance(
        [FromQuery] GetPickPerformanceQuery query, CancellationToken ct = default)
    {
        var response = await mediator
            .RequestAsync<GetPickPerformanceQuery, GetPickPerformanceResponse>(query, ct);

        return Ok(ApiResponse<GetPickPerformanceResponse>.Ok(response));
    }
}
