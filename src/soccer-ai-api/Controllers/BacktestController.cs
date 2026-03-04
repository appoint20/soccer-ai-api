using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SoccerAi.Application.Features.Backtesting;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "CombinedPolicy")]
public class BacktestController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Generate a historical backtest report evaluating accuracy and theoretical combination ROI.
    /// </summary>
    /// <param name="weeksBack">Number of past weeks to analyze (Default: 10)</param>
    /// <param name="stake">Stake per calculated combination (Default: 25.0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Backtest Summary and Accuracy Breakdown</returns>
    [HttpGet]
    [ProducesResponseType<GetBacktestReportResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int weeksBack = 10, [FromQuery] double stake = 25.0, CancellationToken ct = default)
    {
        var response = await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(
            new GetBacktestReportQuery(weeksBack, stake), ct);
            
        return Ok(response);
    }
}
