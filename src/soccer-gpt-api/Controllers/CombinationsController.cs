using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Combinations;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api")]
public class CombinationsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get a high-confidence accumulator (top 3 bets) for a given date.
    /// </summary>
    /// <param name="date">Date (YYYY-MM-DD)</param>
    /// <param name="language">Language code (default 'en')</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("combinations")]
    public async Task<IActionResult> GetCombinations(
        [FromQuery] DateTime date,
        [FromQuery] string language = "en",
        CancellationToken ct = default)
    {
        var query = new GetMatchCombinationQuery(date, language);
        var response = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(query, ct);
        return Ok(response);
    }

    [HttpPost("combinations/custom")]
    public async Task<IActionResult> CreateCustomCombination(
        [FromBody] CreateUserCombinationCommand command,
        CancellationToken ct = default)
    {
        var response = await mediator.SendAsync<CreateUserCombinationCommand, CreateUserCombinationResponse>(command, ct);
        if (!response.Success)
            return BadRequest(response);
            
        return Ok(response);
    }
}
