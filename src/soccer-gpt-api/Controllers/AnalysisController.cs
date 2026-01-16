using Microsoft.AspNetCore.Mvc;
using Mediator.Net;
using soccer_gpt_application.Features.Analysis.Queries;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController(IMediator mediator) : ControllerBase
{
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcomingAnalysis(
        [FromQuery] string? date, 
        [FromQuery] int offset = 0, 
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var filterDate = DateTime.Now;
        if (!string.IsNullOrEmpty(date))
        {
            if (!DateTime.TryParse(date, out var parsed))
                return BadRequest("Invalid date format. Use YYYY-MM-DD.");

            filterDate = parsed;
        }

        var response = await mediator.RequestAsync<GetUpcomingMatchesQuery, GetUpcomingMatchesResponse>(
            new GetUpcomingMatchesQuery 
            { 
                Date = filterDate,
                Offset = offset,
                Limit = limit
            }, 
            cancellationToken);

        return Ok(response.Data.Items);
    }
}
