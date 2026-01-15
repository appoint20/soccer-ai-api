using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Fixtures.Commands;
using soccer_gpt_application.Features.Fixtures.Queries;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FixturesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFixtures(
        [FromQuery] GetFixturesQuery query, CancellationToken cancellationToken = default)
    {
        var result = await mediator.RequestAsync<GetFixturesQuery, GetFixturesResponse>(
            query,
            cancellationToken
        );
        return Ok(result);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFixtures(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest("File is empty or missing.");

        await using var stream = file.OpenReadStream();
        
        var command = new UploadUpcomingFixturesCommand
        {
            FileStream = stream,
            FileName = file.FileName
        };

        try 
        {
            var response = await mediator.SendAsync<UploadUpcomingFixturesCommand, UploadUpcomingFixturesResponse>(
                command,
                cancellationToken
            );
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}