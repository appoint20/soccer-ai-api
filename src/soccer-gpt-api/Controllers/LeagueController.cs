using Microsoft.AspNetCore.Mvc;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/leagues")]
public class LeagueController : ControllerBase
{
    [HttpGet]
    public IActionResult GetLeagues()
    {
        var leagues = new[]
        {
            new { Id = 39, Name = "Premier League" },
            new { Id = 40, Name = "Championship" },
            new { Id = 41, Name = "League One" },
            new { Id = 42, Name = "League Two" },
            new { Id = 78, Name = "Bundesliga" },
            new { Id = 79, Name = "2. Bundesliga" },
            new { Id = 135, Name = "Serie A" },
            new { Id = 136, Name = "Serie B" },
            new { Id = 140, Name = "La Liga" },
            new { Id = 141, Name = "La Liga 2" },
            new { Id = 61, Name = "Ligue 1" },
            new { Id = 62, Name = "Ligue 2" }
        };
        return Ok(leagues);
    }
}
