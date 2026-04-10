using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Loads all match-related data for a fixture: team stats and head-to-head.
/// Extracted from MatchAnalysisService to isolate data access.
/// </summary>
public sealed class MatchDataProvider(
    IApplicationDbContext dbContext,
    ITeamStatsService teamStatsService) : IMatchDataProvider
{
    public async Task<MatchData> LoadAsync(Fixture fixture, CancellationToken ct)
    {
        // ── Fetch Team Metadata (standings/form) ──
        var teams = await dbContext.Teams
            .Where(t => t.ApiId == fixture.HomeTeamId || t.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(t => t.ApiId, t => t, ct);

        var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
        var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);

        // ── Historical matches ──
        var homeLastMatches = await GetLastMatches(fixture.HomeTeamId, fixture.Date, 7, ct);
        var awayLastMatches = await GetLastMatches(fixture.AwayTeamId, fixture.Date, 7, ct);
        var h2HMatches = await GetH2HMatches(
            fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, 5, ct);

        // ── Team stats (weighted — recent matches count more) ──
        var homeStats = teamStatsService.Calculate(fixture.HomeTeamId, homeLastMatches, true);
        var awayStats = teamStatsService.Calculate(fixture.AwayTeamId, awayLastMatches, false);

        // Enrichment
        if (homeTeam != null)
        {
            homeStats.Name = homeTeam.ShortName ?? homeTeam.Name;
            homeStats.Rank = homeTeam.Rank;
            homeStats.Points = homeTeam.Points;
            homeStats.Form = homeTeam.Form;
            homeStats.FormPercentage = CalculateFormPercentage(homeTeam.Form);
        }

        if (awayTeam != null)
        {
            awayStats.Name = awayTeam.ShortName ?? awayTeam.Name;
            awayStats.Rank = awayTeam.Rank;
            awayStats.Points = awayTeam.Points;
            awayStats.Form = awayTeam.Form;
            awayStats.FormPercentage = CalculateFormPercentage(awayTeam.Form);
        }

        var teamStats = new TeamStatsResponse { Home = homeStats, Away = awayStats };
        var h2HModel = CalculateH2H(h2HMatches, fixture.HomeTeamId);

        return new MatchData
        {
            TeamStats = teamStats,
            H2H = h2HModel,
            HomeRestDays = CalculateRestDays(fixture, homeLastMatches),
            AwayRestDays = CalculateRestDays(fixture, awayLastMatches)
        };
    }

    private static int CalculateFormPercentage(string form)
    {
        if (string.IsNullOrWhiteSpace(form)) return 0;
        
        // Take the last 5 results if string is longer
        var recent = form.Length > 5 ? form.Substring(form.Length - 5) : form;
        
        double maxPoints = recent.Length * 3;
        double earnedPoints = 0;

        foreach (var result in recent.ToUpperInvariant())
        {
            if (result == 'W') earnedPoints += 3;
            else if (result == 'D') earnedPoints += 1;
        }

        return (int)Math.Round((earnedPoints / maxPoints) * 100);
    }

    private static float? CalculateRestDays(Fixture fixture, List<Fixture> lastMatches)
    {
        if (lastMatches == null || lastMatches.Count == 0) return null;
        var lastMatch = lastMatches.OrderByDescending(m => m.Date).First();
        return (float)(fixture.Date - lastMatch.Date).TotalDays;
    }

    // ── H2H Calculation ──────────────────────────────────────────

    private static HeadToHeadModel CalculateH2H(List<Fixture>? matches, int homeId)
    {
        if (matches == null || matches.Count == 0) return HeadToHeadModel.Empty;

        double homeGoals = 0, awayGoals = 0, totalGoals = 0;
        int btts = 0, over25 = 0, twoToThree = 0;
        int homeWins = 0, awayWins = 0, draws = 0;
        DateTimeOffset? lastMatchDate = null;

        foreach (var m in matches)
        {
            var hg = m.HomeTeamId == homeId ? m.HomeGoal : m.AwayGoal;
            var ag = m.HomeTeamId == homeId ? m.AwayGoal : m.HomeGoal;
            homeGoals += hg;
            awayGoals += ag;
            var matchTotal = hg + ag;
            totalGoals += matchTotal;

            if (hg > ag) homeWins++;
            else if (ag > hg) awayWins++;
            else draws++;

            if (hg > 0 && ag > 0) btts++;
            if (matchTotal > 2.5) over25++;
            if (matchTotal >= 2 && matchTotal <= 3) twoToThree++;

            if (lastMatchDate == null || m.Date > lastMatchDate)
                lastMatchDate = m.Date;
        }

        return new HeadToHeadModel
        {
            MatchesAnalyzed = matches.Count,
            AvgGoalsHome = Math.Round(homeGoals / matches.Count, 2),
            AvgGoalsAway = Math.Round(awayGoals / matches.Count, 2),
            AvgTotalGoals = Math.Round(totalGoals / matches.Count, 2),
            BTTSRate = Math.Round((double)btts / matches.Count, 2),
            Over25Rate = Math.Round((double)over25 / matches.Count, 2),
            TwoToThreeGoalsRate = Math.Round((double)twoToThree / matches.Count, 2),
            HomeWinRate = Math.Round((double)homeWins / matches.Count, 2),
            AwayWinRate = Math.Round((double)awayWins / matches.Count, 2),
            DrawRate = Math.Round((double)draws / matches.Count, 2),
            LastMatchDate = lastMatchDate
        };
    }

    // ── DB Queries ───────────────────────────────────────────────

    private async Task<List<Fixture>> GetLastMatches(int teamId, DateTimeOffset before, int count, CancellationToken ct)
    {
        // SQLite/EF Core does not support DateTimeOffset in ORDER BY clauses.
        // We fetch matches and sort them in-memory.
        var matches = await dbContext.Fixtures
            .Where(f => (f.HomeTeamId == teamId || f.AwayTeamId == teamId) && f.Status == "FT")
            .ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(count)
            .ToList();
    }

    private async Task<List<Fixture>?> GetH2HMatches(int teamA, int teamB, DateTimeOffset before, int count, CancellationToken ct)
    {
        // Similar simplification for H2H matches to avoid SQLite translation/ordering issues
        var matches = await dbContext.Fixtures
            .Where(f => ((f.HomeTeamId == teamA && f.AwayTeamId == teamB) ||
                         (f.HomeTeamId == teamB && f.AwayTeamId == teamA)) && f.Status == "FT")
            .ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(count)
            .ToList();
    }
}
