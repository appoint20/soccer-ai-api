
using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Predictions.Queries;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.Llm;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/v1/predictions")]
public class PredictionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<LlmMatchDataset>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPredictions([FromQuery] int offset = 0, [FromQuery] int limit = 10)
    {
        var response = await mediator.RequestAsync<GetPredictionsQuery, GetPredictionsResponse>(
            new GetPredictionsQuery { Offset = offset, Limit = limit });
        return Ok(response.Data);
    }
}
