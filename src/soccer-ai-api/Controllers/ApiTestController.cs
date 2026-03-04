using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "CombinedPolicy")]
public class ApiTestController(IApiFootballService apiService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> TestConnection()
    {
        var result = await apiService.TestConnectionAsync();
        return Ok(result);
    }
}
