using Mediator.Net;
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
}

public record LoginRequest(string Username, string Password);
