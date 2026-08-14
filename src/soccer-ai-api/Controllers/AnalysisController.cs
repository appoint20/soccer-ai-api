using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/analyze")]
[Authorize(Policy = "CombinedPolicy")]
public class AnalysisController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get detailed match analysis including stats, form, H2H, Poisson, and ML predictions.
    /// </summary>
    /// <remarks>
    /// Paged by default: omitting <c>limit</c> returns the first
    /// <see cref="SoccerAi.Application.Models.PageRequest.DefaultLimit"/> fixtures,
    /// not the whole date. Each fixture on the page can trigger a snapshot
    /// recompute, so an unbounded page is a slow request by construction.
    /// </remarks>
    /// <param name="query">Date, language, filters and the <c>limit</c>/<c>offset</c> window.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet]
    [ProducesResponseType<ApiResponse<GetMatchAnalysisResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] GetMatchAnalysisQuery query, CancellationToken ct = default)
    {
        var response = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(query, ct);
        return Ok(ApiResponse<GetMatchAnalysisResponse>.Ok(response));
    }

    /// <summary>
    /// Audit endpoint to check how many upcoming fixtures possess AI Analysis.
    /// </summary>
    /// <param name="query">The <c>days_ahead</c> window plus <c>limit</c>/<c>offset</c>.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("audit/ai-coverage")]
    [ProducesResponseType<ApiResponse<GetAiCoverageResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiCoverage(
        [FromQuery] GetAiCoverageQuery query, CancellationToken ct = default)
    {
        var response = await mediator.RequestAsync<GetAiCoverageQuery, GetAiCoverageResponse>(query, ct);
        return Ok(ApiResponse<GetAiCoverageResponse>.Ok(response));
    }
}
