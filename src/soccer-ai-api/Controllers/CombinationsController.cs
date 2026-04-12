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
    /// Unified entry point for all combination requests.
    /// Handles both custom chat queries and automatic 'SYSTEM' daily recommendations.
    /// </summary>
    /// <param name="request">Contains the natural language query. If empty, returns top SYSTEM portfolios.</param>
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
