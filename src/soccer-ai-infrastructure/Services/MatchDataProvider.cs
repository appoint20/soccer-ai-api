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
            homeStats.Played = homeTeam.Played;
            homeStats.Form = homeTeam.Form;
            homeStats.FormPercentage = CalculateFormPercentage(homeTeam.Form);
            homeStats.MotivationScore = CalculateMotivation(homeTeam.Points, homeTeam.Played, 38);
            homeStats.IsNewManager = homeTeam.ManagerAppointedAt.HasValue && (fixture.Date - homeTeam.ManagerAppointedAt.Value).TotalDays <= 30;
            homeStats.HasRedCardHangover = homeLastMatches.Any() && ((homeLastMatches[0].HomeTeamId == fixture.HomeTeamId && homeLastMatches[0].HomeRedCards > 0) || (homeLastMatches[0].AwayTeamId == fixture.HomeTeamId && homeLastMatches[0].AwayRedCards > 0));
        }

        if (awayTeam != null)
        {
            awayStats.Name = awayTeam.ShortName ?? awayTeam.Name;
            awayStats.Rank = awayTeam.Rank;
            awayStats.Points = awayTeam.Points;
            awayStats.Played = awayTeam.Played;
            awayStats.Form = awayTeam.Form;
            awayStats.FormPercentage = CalculateFormPercentage(awayTeam.Form);
            awayStats.MotivationScore = CalculateMotivation(awayTeam.Points, awayTeam.Played, 38);
            awayStats.IsNewManager = awayTeam.ManagerAppointedAt.HasValue && (fixture.Date - awayTeam.ManagerAppointedAt.Value).TotalDays <= 30;
            awayStats.HasRedCardHangover = awayLastMatches.Any() && ((awayLastMatches[0].HomeTeamId == fixture.AwayTeamId && awayLastMatches[0].HomeRedCards > 0) || (awayLastMatches[0].AwayTeamId == fixture.AwayTeamId && awayLastMatches[0].AwayRedCards > 0));
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

    private static double CalculateMotivation(int points, int played, int totalGames)
    {
        if (played < 10) return 5.0; // Early season neutral
        
        double ppg = (double)points / played;
        
        if (ppg < 1.0) return 10.0; // Survival
        if (ppg > 1.8) return 9.0;  // Title/Europe
        if (ppg > 1.2 && ppg < 1.5) return 2.0; // Dead mid-table
        
        return 5.0;
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
        // Filter by date in DB to avoid loading entire history for every team
        // We still sort/take in-memory due to SQLite DateTimeOffset ordering limitations in older EF versions
        var matches = await dbContext.Fixtures
            .Where(f => (f.HomeTeamId == teamId || f.AwayTeamId == teamId) && f.Status == "FT" && f.Date < before)
            .ToListAsync(ct);

        return matches
            .OrderByDescending(f => f.Date)
            .Take(count)
            .ToList();
    }

    private async Task<List<Fixture>?> GetH2HMatches(int teamA, int teamB, DateTimeOffset before, int count, CancellationToken ct)
    {
        var matches = await dbContext.Fixtures
            .Where(f => ((f.HomeTeamId == teamA && f.AwayTeamId == teamB) ||
                         (f.HomeTeamId == teamB && f.AwayTeamId == teamA)) && f.Status == "FT" && f.Date < before)
            .ToListAsync(ct);

        return matches
            .OrderByDescending(f => f.Date)
            .Take(count)
            .ToList();
    }
}
