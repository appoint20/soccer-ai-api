using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_api.Controllers;

/// <summary>
/// Controller for enriched fixture ingestion and querying
/// </summary>
[ApiController]
[Route("api/enriched-fixtures")]
public class EnrichedFixtureController : ControllerBase
{
    private readonly IEnrichedFixtureIngestionService _ingestionService;
    private readonly ILogger<EnrichedFixtureController> _logger;

    public EnrichedFixtureController(
        IEnrichedFixtureIngestionService ingestionService,
        ILogger<EnrichedFixtureController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Ingest all English leagues for a given season
    /// </summary>
    [HttpPost("ingest/{season:int}")]
    public async Task<ActionResult<IngestionResult>> IngestEnglishLeagues(int season)
    {
        _logger.LogInformation("Starting ingestion for season {Season}", season);
        
        try
        {
            var result = await _ingestionService.IngestEnglishLeaguesAsync(season);
            
            _logger.LogInformation(
                "Ingestion complete. Saved: {Saved}, Skipped: {Skipped}, Duration: {Duration}",
                result.Saved, result.Skipped, result.Duration);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for season {Season}", season);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Ingest a specific league for a given season
    /// </summary>
    [HttpPost("ingest/{leagueId:int}/{season:int}")]
    public async Task<ActionResult<IngestionResult>> IngestLeague(int leagueId, int season)
    {
        // Validate English leagues only
        var validLeagues = new[] { 39, 40, 41, 42 };
        if (!validLeagues.Contains(leagueId))
        {
            return BadRequest(new { error = $"Only English leagues are supported. Valid IDs: {string.Join(", ", validLeagues)}" });
        }

        _logger.LogInformation("Starting ingestion for league {LeagueId}, season {Season}", leagueId, season);
        
        try
        {
            var result = await _ingestionService.IngestLeagueAsync(leagueId, season);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for league {LeagueId}, season {Season}", leagueId, season);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
