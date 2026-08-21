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
    /// Prices a slip the user assembled themselves.
    /// </summary>
    /// <remarks>
    /// The client can multiply the leg prices exactly, so total odds is not why
    /// this exists — the joint probability is. Multiplying leg probabilities
    /// assumes independence, which is false for two markets on one fixture, and
    /// EV and Kelly computed from a wrong joint are numbers a user might stake
    /// against. Returns the same <c>TicketDto</c> a generated combination uses,
    /// so the builder renders through one component.
    ///
    /// Stateless — nothing is stored, and the slip stays on the device.
    /// </remarks>
    /// <response code="400">
    /// A leg has no published price, names an unknown or informational-only
    /// market, or pairs two correlated markets on one fixture that the model
    /// has no joint probability for. The message names the offending leg.
    /// </response>
    [HttpPost("custom")]
    [ProducesResponseType<ApiResponse<TicketDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PriceCustomTicket(
        [FromBody] PriceCustomTicketQuery query,
        [FromQuery] string? language = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Language = language;

        var response = await mediator
            .RequestAsync<PriceCustomTicketQuery, PriceCustomTicketResponse>(query, ct);

        if (response.Error is not null)
            return BadRequest(ApiResponse<object>.Fail(response.Error));

        return Ok(ApiResponse<TicketDto>.Ok(response.Ticket!));
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
