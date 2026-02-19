using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApiTestController(IApiFootballService apiService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> TestConnection()
    {
        var result = await apiService.TestConnectionAsync();
        return Ok(result);
    }
}
