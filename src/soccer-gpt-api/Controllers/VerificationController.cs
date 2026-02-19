using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/verify")]
public class VerificationController(
    IApplicationDbContext db,
    FixtureSyncService fixtureSyncService) : ControllerBase
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

    [HttpGet("team/{id}")]
    public async Task<IActionResult> GetTeam(int id)
    {
        var team = await db.Teams
            .FirstOrDefaultAsync(t => t.ApiId == id);
            
        if (team == null) return NotFound($"Team with API ID {id} not found.");
        
        return Ok(team);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var teamCount = await db.Teams.CountAsync();
        var fixtureCount = await db.Fixtures.CountAsync();
        
        return Ok(new {
            Teams = teamCount,
            Fixtures = fixtureCount
        });
    }

    [HttpPost("sync/fixtures/{leagueId}")]
    public async Task<IActionResult> SyncFixtures(int leagueId, [FromQuery] int season = 2024, CancellationToken ct = default)
    {
        var result = await fixtureSyncService.SyncLeagueFixturesAsync(leagueId, season, ct);
        return Ok(result);
    }

    [HttpGet("sync/fixtures")]
    public async Task<IActionResult> SyncAllFixtures([FromQuery] int season = 2024, CancellationToken ct = default)
    {
        var result = await fixtureSyncService.SyncAllLeaguesAsync(season, ct);
        return Ok(result);
    }

    [HttpGet("excel-stats")]
    public async Task<IActionResult> GetExcelStats([FromServices] IHistoricalDataService historicalDataService)
    {
        var stats = await historicalDataService.GetAvailableDivisionsAsync();
        return Ok(stats);
    }

    [HttpPost("sync/standings/{leagueId}")]
    public async Task<IActionResult> SyncStandings(
        [FromServices] TeamSyncService teamSyncService,
        int leagueId, 
        [FromQuery] int season = 2024, 
        CancellationToken ct = default)
    {
        var result = await teamSyncService.SyncLeagueStandingsAsync(leagueId, season, ct);
        return Ok(result);
    }

    [HttpGet("sync/standings")]
    public async Task<IActionResult> SyncAllStandings(
        [FromServices] TeamSyncService teamSyncService,
        [FromQuery] int season = 2024, 
        CancellationToken ct = default)
    {
        var result = await teamSyncService.SyncAllLeaguesAsync(season, ct);
        return Ok(result);
    }
}
