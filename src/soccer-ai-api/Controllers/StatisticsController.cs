using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Statistics;

namespace SoccerAi.Api.Controllers;

[ApiController, Route("api/statistics"), Authorize(Policy = "CombinedPolicy")]
public sealed class StatisticsController(PredictionStatisticsService statistics) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 90, CancellationToken ct = default)
    {
        if (days is < 7 or > 730) return BadRequest(ApiResponse<object>.Fail("days must be between 7 and 730"));
        return Ok(ApiResponse<PredictionStatistics>.Ok(await statistics.GetAsync(days, ct)));
    }
}
