using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Api.Controllers;

[ApiController]
[Route("api/briefings")]
[Authorize(Policy = "CombinedPolicy")]
public sealed class BriefingsController(IApplicationDbContext database, FixtureQueryHelper fixtures) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] BriefingQuery query, CancellationToken ct)
    {
        if (!ValidQuery(query)) return BadRequest(ApiResponse<object>.Fail("Use en/de, a non-negative offset and a positive page."));
        var limit = query.ResolveLimit();
        var offset = query.ResolveOffset();
        var (page, teams, total) = await fixtures.GetFixturesWithTeamsAsync(query.Date ?? DateTimeOffset.UtcNow,
            limit, offset, cancellationToken: ct);
        var ids = page.Select(match => match.Id).ToList();
        var rows = await database.FixtureAnalyses.AsNoTracking()
            .Where(row => ids.Contains(row.FixtureId) && row.Lang == query.Language)
            .ToDictionaryAsync(row => row.FixtureId, ct);
        var matches = page.Select(fixture => Read(fixture, rows.GetValueOrDefault(fixture.Id), teams, query.Language)).ToList();
        return Ok(ApiResponse<object>.Ok(new
        {
            matches, limit, offset, total, has_more = offset + matches.Count < total,
            mode = "analysis", provider_refresh_requested = false
        }));
    }

    [HttpGet("{fixtureId:int}")]
    public async Task<IActionResult> GetOne(int fixtureId, [FromQuery] string language = "en", CancellationToken ct = default)
    {
        if (language is not ("en" or "de")) return BadRequest(ApiResponse<object>.Fail("Use en or de."));
        var fixture = await database.Fixtures.AsNoTracking().SingleOrDefaultAsync(row => row.Id == fixtureId, ct);
        if (fixture is null) return NotFound(ApiResponse<object>.Fail("Unknown fixture."));
        var row = await database.FixtureAnalyses.AsNoTracking()
            .SingleOrDefaultAsync(row => row.FixtureId == fixtureId && row.Lang == language, ct);
        var teams = await database.Teams.AsNoTracking()
            .Where(team => team.ApiId == fixture.HomeTeamId || team.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(team => team.ApiId, ct);
        return Ok(ApiResponse<object>.Ok(new { match = Read(fixture, row, teams, language) }));
    }

    [HttpGet("{fixtureId:int}/history")]
    public async Task<IActionResult> History(int fixtureId, CancellationToken ct)
    {
        var fixture = await database.Fixtures.AsNoTracking().SingleOrDefaultAsync(row => row.Id == fixtureId, ct);
        if (fixture is null) return NotFound(ApiResponse<object>.Fail("Unknown fixture."));
        var now = DateTimeOffset.UtcNow;
        var records = await database.PredictionSnapshots.AsNoTracking()
            .Where(row => row.FixtureId == fixtureId && row.KickoffUtc == fixture.Date &&
                row.CapturedAtUtc < row.KickoffUtc && row.CapturedAtUtc <= now)
            .OrderByDescending(row => row.CapturedAtUtc).ThenByDescending(row => row.Id).Take(50).ToListAsync(ct);
        var entries = records.Where(row => MatchBriefingFactory.ValidDistribution(row.Home, row.Draw, row.Away)
            && new[] { row.Btts, row.Over25, row.Goals23 }.All(MatchBriefingFactory.ValidProbability))
            .Select(row => new BriefingHistoryEntry(row.CapturedAtUtc, row.KickoffUtc, row.ModelVersion,
                [new("home", row.Home), new("draw", row.Draw), new("away", row.Away)],
                [new("btts", row.Btts), new("over25", row.Over25), new("goals_2_3", row.Goals23)])).ToList();
        return Ok(ApiResponse<object>.Ok(new
        {
            fixture_id = fixtureId, entries, basis = "recorded_pre_match_estimates",
            includes_reconstructed_history = false,
            note = "Only recorded estimates for the current kickoff are shown. Changes do not establish their cause or betting profitability."
        }));
    }

    private static MatchBriefing Read(Fixture fixture, FixtureAnalysis? row, Dictionary<int, Team> teams, string language)
    {
        var home = teams.GetValueOrDefault(fixture.HomeTeamId)?.Name ?? $"Team {fixture.HomeTeamId}";
        var away = teams.GetValueOrDefault(fixture.AwayTeamId)?.Name ?? $"Team {fixture.AwayTeamId}";
        var snapshot = AnalysisSnapshotSerializer.Deserialize(row?.SnapshotJson);
        var usable = snapshot is not null && snapshot.Id == fixture.Id && snapshot.Date == fixture.Date
            && snapshot.HomeTeam == home && snapshot.AwayTeam == away;
        if (!usable)
            snapshot = new MatchAnalysis
            {
                Id = fixture.Id, Date = fixture.Date, League = LeagueCatalog.Name(fixture.LeagueId),
                HomeTeam = home, AwayTeam = away
            };
        SoccerAi.Application.Services.LiveOddsPolicy.RefreshResponse(snapshot!, fixture, DateTimeOffset.UtcNow);
        return MatchBriefingFactory.Create(snapshot!, language, usable ? row?.UpdatedAt : null);
    }

    private static bool ValidQuery(BriefingQuery query) => query.Language is "en" or "de"
        && query.Offset is not < 0 && query.Page is not < 1 && query.Page is not > 1000000;
}

public sealed class BriefingQuery : PageRequest
{
    public DateTimeOffset? Date { get; set; }
    public string Language { get; set; } = "en";
}
