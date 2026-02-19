using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Predictions;

namespace soccer_gpt_api.Controllers;

/// <summary>
/// Thin controller for fixture predictions using CQRS pattern.
/// </summary>
[ApiController]
[Route("api/fixtures")]
public class FixturePredictionsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get ML predictions for fixtures on a specific date and league.
    /// </summary>
    /// <param name="date">Date to get predictions for (YYYY-MM-DD)</param>
    /// <param name="leagueId">League ID (e.g., 39 for Premier League)</param>
    /// <param name="language"></param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("predictions")]
    public async Task<IActionResult> GetPredictions(
        [FromQuery] DateTime date,
        [FromQuery] int leagueId,
        [FromQuery] string language,
        CancellationToken ct)
    {
        var query = new GetFixturePredictionsQuery
        {
            Date = date,
            LeagueId = leagueId,
            Language = language
        };

        var response = await mediator.RequestAsync<GetFixturePredictionsQuery, GetFixturePredictionsResponse>(query, ct);

        return Ok(response);
    }
}
