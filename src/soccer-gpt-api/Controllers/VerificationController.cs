using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/verify")]
public class VerificationController(
    IApplicationDbContext db) : ControllerBase
{
    [HttpGet("fixtures")]
    public async Task<IActionResult> GetFixtures([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var fixtures = await db.Fixtures
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(f => new {
                f.Id,
                f.ApiId,
                f.LeagueId,
                f.HomeTeamId,
                f.AwayTeamId,
                f.HomeGoal,
                f.AwayGoal,
                f.HomeXg,
                f.AwayXg,
                f.CreatedAt
            })
            .ToListAsync();
            
        return Ok(new { Count = fixtures.Count, Data = fixtures });
    }

    [HttpGet("teams")]
    public async Task<IActionResult> GetTeams([FromQuery] int limit = 50, [FromQuery] int offset = 0, [FromQuery] int? leagueId = null)
    {
        var query = db.Teams.AsQueryable();
        
        if (leagueId.HasValue)
            query = query.Where(t => t.LeagueId == leagueId.Value);
        
        var teams = await query
            .OrderBy(t => t.Name)
            .Skip(offset)
            .Take(limit)
            .Select(t => new {
                t.Id,
                t.ApiId,
                t.Name,
                t.LeagueId,
                t.Rank,
                t.Points,
                t.Form
            })
            .ToListAsync();
            
        return Ok(new { Count = teams.Count, Data = teams });
    }

    [HttpPost("sync/fixtures/{leagueId}")]
    public async Task<IActionResult> SyncFixtures(int leagueId, [FromQuery] int season, [FromServices] FixtureSyncService syncService)
    {
        var result = await syncService.SyncLeagueFixturesAsync(leagueId, season, CancellationToken.None);
        return Ok(new { Created = result.Created, Updated = result.Updated, Errors = result.Errors });
    }

    [HttpPost("sync/standings/{leagueId}")]
    public async Task<IActionResult> SyncStandings(int leagueId, [FromQuery] int season, [FromServices] TeamSyncService syncService)
    {
        var result = await syncService.SyncLeagueStandingsAsync(leagueId, season, CancellationToken.None);
        return Ok(new { Created = result.Created, Updated = result.Updated, Errors = result.Errors });
    }
}
