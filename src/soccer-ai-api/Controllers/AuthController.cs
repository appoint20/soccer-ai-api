using System.Security.Claims;
using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Auth;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = await mediator.SendAsync<LoginCommand, LoginResponse>(new LoginCommand(request.Username, request.Password));
            return Ok(ApiResponse<object>.Ok(new { token = response.Token }));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResponse<object>.Fail("Invalid username or password"));
        }
    }

    /// <summary>
    /// Hard-deletes the authenticated user's account after confirming their
    /// password. Required by Google Play and Apple guideline 5.1.1(v).
    /// </summary>
    /// <remarks>
    /// Returns <b>403</b> on a wrong password — not 401 — so the app's global
    /// session-expired handler is not tripped by a typo.
    /// </remarks>
    [HttpDelete("account")]
    [Authorize(Policy = "JwtPolicy")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteAccount(
        [FromBody] DeleteAccountRequest request, CancellationToken ct = default)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

        var response = await mediator.SendAsync<DeleteAccountCommand, DeleteAccountResponse>(
            new DeleteAccountCommand(userId, request.Password), ct);

        return Ok(ApiResponse<object>.Ok(new { deleted = response.Deleted }));
    }
}

public record LoginRequest(string Username, string Password);
public record DeleteAccountRequest(string Password);

