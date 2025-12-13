
using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Matches.Queries;
using soccer_gpt_application.Models;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MatchesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Gets upcoming matches with H2H analysis and stats.
    /// </summary>
    /// <param name="offset">Offset for pagination</param>
    /// <param name="limit">Limit for pagination</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Paged list of matches</returns>
    [HttpGet("upcoming")]
    [ProducesResponseType(typeof(PagedResponse<UpcomingMatchDto>), 200)]
    public async Task<IActionResult> GetUpcomingMatches([FromQuery] int offset = 0, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var response = await mediator.RequestAsync<GetUpcomingMatchesQuery, GetUpcomingMatchesResponse>(
            new GetUpcomingMatchesQuery { Offset = offset, Limit = limit }, 
            cancellationToken);
            
        return Ok(response.Data);
    }
}
