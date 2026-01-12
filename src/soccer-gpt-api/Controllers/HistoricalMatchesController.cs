using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Matches.Queries;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/historical-matches")]
public class HistoricalMatchesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] GetHistoricalMatchesQuery query, CancellationToken cancellationToken = default)
    {
        var result = await mediator.RequestAsync<GetHistoricalMatchesQuery, GetHistoricalMatchesResponse>(query, cancellationToken);
        return Ok(result);
    }
}
