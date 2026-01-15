using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.HistoricalMatches.Commands;
using soccer_gpt_application.Features.HistoricalMatches.Query;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/historical")]
public class HistoricalDataController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetHistoricalMatches(
        [FromQuery] GetHistoricalMatchesQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator
            .RequestAsync<GetHistoricalMatchesQuery, GetHistoricalMatchesResponse>(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadHistoricalData(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File is empty or missing.");
        }

        await using var stream = file.OpenReadStream();
        
        var command = new UploadHistoricalDataCommand
        {
            FileStream = stream,
            FileName = file.FileName
        };

        var response = await mediator.SendAsync<UploadHistoricalDataCommand, UploadHistoricalDataResponse>(command, cancellationToken);
        
        return Ok(response);
    }
}
