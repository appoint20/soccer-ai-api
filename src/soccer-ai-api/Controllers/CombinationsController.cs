using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CombinationsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GetMatchCombinationResponse>> GetCombinations([FromBody] GetMatchCombinationQuery query, CancellationToken ct = default)
    {
        // 1. Delegate to the AI-driven Combination Engine via Mediator
        // This fixes the DI error for IMatchRepository as the engine uses the DB context directly.
        var response = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(query, ct);

        // 2. Return results in the standard response wrapper
        return Ok(response);
    }
}
