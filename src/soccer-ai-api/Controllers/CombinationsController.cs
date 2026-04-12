using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = "CombinedPolicy")]
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
        [FromQuery] DateTimeOffset? date = null,
        [FromQuery] string language = "en",
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        var targetDate = date ?? DateTimeOffset.UtcNow;
        var query = new GetMatchCombinationQuery(targetDate, language, refresh);
        var response = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(query, ct);
        return Ok(ApiResponse<GetMatchCombinationResponse>.Ok(response));
    }

    [HttpPost("combinations/custom")]
    public async Task<IActionResult> CreateCustomCombination(
        [FromBody] CreateUserCombinationCommand command,
        CancellationToken ct = default)
    {
        var response = await mediator.SendAsync<CreateUserCombinationCommand, CreateUserCombinationResponse>(command, ct);
        if (!response.Success)
            return BadRequest(ApiResponse<CreateUserCombinationResponse>.Fail(response.Message ?? "Failed to create combination"));
            
        return Ok(ApiResponse<CreateUserCombinationResponse>.Ok(response));
    }

    [HttpPost("combinations")]
    public async Task<IActionResult> CreateChatCombination(
        [FromBody] CreateChatCombinationRequest request,
        CancellationToken ct = default)
    {
        var command = new CreateChatCombinationCommand { Query = request.Query };
        var response = await mediator.SendAsync<CreateChatCombinationCommand, CreateChatCombinationResponse>(command, ct);
        
        if (!response.Success)
            return BadRequest(ApiResponse<CreateChatCombinationResponse>.Fail(response.Message));
            
        return Ok(ApiResponse<CreateChatCombinationResponse>.Ok(response));
    }

    public class CreateChatCombinationRequest
    {
        public string Query { get; set; } = string.Empty;
    }
}
