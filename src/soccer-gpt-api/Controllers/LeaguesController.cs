using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Leagues.Queries;
using soccer_gpt_application.Models;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class LeaguesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<LeagueDto>), 200)]
    public async Task<IActionResult> GetLeagues(CancellationToken cancellationToken)
    {
        var response = await mediator.RequestAsync<GetLeaguesQuery, GetLeaguesResponse>(
            new GetLeaguesQuery(), cancellationToken);
        
        return Ok(response.Data);
    }
}
