using System.Security.Claims;
using Mediator.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Features.Auth;
using SoccerAi.Application.Interfaces;
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
        [FromBody] DeleteAccountRequest request,
        [FromServices] ISupabaseAdminService supabase,
        CancellationToken ct = default)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        if (userIdClaim is null)
            return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

        // A Supabase user id is a UUID and there is no row for them in this
        // database — the account lives in Supabase, so the deletion has to
        // happen there. The password is still re-checked first: an unlocked
        // phone must not be enough to delete an account.
        if (Guid.TryParse(userIdClaim, out _))
        {
            if (!supabase.IsConfigured)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<object>.Fail("Account deletion is not available right now."));

            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

            if (!await supabase.VerifyPasswordAsync(email, request.Password, ct))
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<object>.Fail("Incorrect password."));

            var removed = await supabase.DeleteUserAsync(userIdClaim, ct);
            if (!removed)
                return StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<object>.Fail("The account could not be deleted. Please try again."));

            return Ok(ApiResponse<object>.Ok(new { deleted = true }));
        }

        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

        var response = await mediator.SendAsync<DeleteAccountCommand, DeleteAccountResponse>(
            new DeleteAccountCommand(userId, request.Password), ct);

        return Ok(ApiResponse<object>.Ok(new { deleted = response.Deleted }));
    }
}

public record LoginRequest(string Username, string Password);
public record DeleteAccountRequest(string Password);

