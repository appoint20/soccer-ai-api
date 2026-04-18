using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models.Deterministic;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CombinationsController(
    INlpService nlpService,
    IMatchRepository matchRepository,
    ICombinationService combinationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CombinationResponse>> GetCombinations([FromBody] CombinationRequest request)
    {
        // 1. NLP Integration: Parse natural language input (Service now handles empty query)
        var intent = await nlpService.ParseIntentAsync(request.Query);
        
        // 2. Data Source: Fetch available matches (Filter by Date if provided)
        var matches = await matchRepository.GetUpcomingMatchesAsync(request.Date);

        // 3. Combination Engine: Generate, score and rank
        var combinations = combinationService.GenerateCombinations(matches, intent);

        // 4. Return results
        return Ok(new CombinationResponse { Combinations = combinations });
    }
}
