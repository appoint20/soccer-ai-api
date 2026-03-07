using System.Threading;
using System.Threading.Tasks;
using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/analyze")]
[Authorize(Policy = "CombinedPolicy")]
public class AnalysisController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get detailed match analysis including stats, form, H2H, Poisson, and ML predictions.
    /// </summary>
    /// <param name="date">Date to analyze (YYYY-MM-DD)</param>
    /// <param name="language">Language code</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet]
    [ProducesResponseType<GetMatchAnalysisResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] GetMatchAnalysisQuery query, CancellationToken ct = default)
    {
        var response = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(query, ct);
        return Ok(response);
    }

    /// <summary>
    /// Audit endpoint to check how many upcoming fixtures possess Gemini Analysis.
    /// </summary>
    /// <param name="daysAhead">Number of days to look ahead (default 5)</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("audit/gemini-coverage")]
    [ProducesResponseType<GetGeminiCoverageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGeminiCoverage([FromQuery] int daysAhead = 5, CancellationToken ct = default)
    {
        var query = new GetGeminiCoverageQuery { DaysAhead = daysAhead };
        var response = await mediator.RequestAsync<GetGeminiCoverageQuery, GetGeminiCoverageResponse>(query, ct);
        return Ok(response);
    }
}
