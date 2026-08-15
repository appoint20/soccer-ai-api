using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Features.Forecasts;
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
    /// One fixture's analysis, by id — for the match detail screen.
    /// </summary>
    /// <remarks>
    /// Works for any date, past or future. Use this rather than searching the
    /// date-scoped list: a fixture kicking off tomorrow is not in today's list,
    /// which is why opening it reported the match as missing from the analysis.
    /// Returns 404 when the fixture is unknown or has no analysis yet.
    /// </remarks>
    /// <param name="fixtureId">Fixture id, as returned in <c>items[].id</c>.</param>
    /// <param name="query">Optional language.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("{fixtureId:int}")]
    [ProducesResponseType<ApiResponse<GetFixtureAnalysisResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByFixture(
        int fixtureId, [FromQuery] GetFixtureAnalysisQuery query, CancellationToken ct = default)
    {
        query.FixtureId = fixtureId;

        var response = await mediator
            .RequestAsync<GetFixtureAnalysisQuery, GetFixtureAnalysisResponse>(query, ct);

        if (response.Match is null)
            return NotFound(ApiResponse<object>.Fail($"No analysis available for fixture {fixtureId}."));

        return Ok(ApiResponse<GetFixtureAnalysisResponse>.Ok(response));
    }

    /// <summary>
    /// How the language models have scored against the statistical pipeline on
    /// settled fixtures.
    /// </summary>
    /// <remarks>
    /// Ranked on Brier score, not hit rate: both sides output probabilities, and
    /// Brier is a proper scoring rule — it rewards reporting a true belief
    /// rather than hedging toward 0.5 or overstating confidence. Hit rate is
    /// returned alongside for display only. No leader is named until at least
    /// two forecasters clear the sample threshold.
    /// </remarks>
    /// <param name="query">Optional <c>from</c>/<c>to</c> kickoff date range.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("forecast-scoreboard")]
    [ProducesResponseType<ApiResponse<GetForecastScoreboardResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForecastScoreboard(
        [FromQuery] GetForecastScoreboardQuery query, CancellationToken ct = default)
    {
        var response = await mediator
            .RequestAsync<GetForecastScoreboardQuery, GetForecastScoreboardResponse>(query, ct);

        return Ok(ApiResponse<GetForecastScoreboardResponse>.Ok(response));
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
