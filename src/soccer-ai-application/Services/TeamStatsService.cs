using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services;

public interface ITeamStatsService
{
    TeamStats Calculate(int teamId, IEnumerable<Fixture> allMatches, bool isHomeTeam);
}

public class TeamStatsService: ITeamStatsService
{
    private static class Config
    {
        public const int OverallWindow = 7;
        public const int RecentWindow = 3;
        public const int VenueWindow = 3;
        public const int Over25Threshold = 3;
    }

    public TeamStats Calculate(int teamId, IEnumerable<Fixture> allMatches, bool isHomeTeam)
    {
        ArgumentNullException.ThrowIfNull(allMatches);
        if (teamId <= 0)
            throw new ArgumentException("Team ID must be positive");

        var ordered = allMatches
            .OrderByDescending(x => x.Date)
            .ToList();

        var last7Overall = ordered.Take(Config.OverallWindow).ToList();
        var last3Overall = ordered.Take(Config.RecentWindow).ToList();

        var venueMatches = ordered
            .Where(m =>
                isHomeTeam ? m.HomeTeamId == teamId : m.AwayTeamId == teamId)
            .Take(Config.VenueWindow)
            .ToList();

        var venueAttack = AvgScored(venueMatches, teamId);
        var venueConceded = AvgConceded(venueMatches, teamId);
        var overallAttack = AvgScored(last7Overall, teamId);
        var overallConceded = AvgConceded(last7Overall, teamId);

        return new TeamStats
        {
            // LAST 3 OVERALL (Updated from Venue-only as requested)
            AvgGoalsScoredLast3 = AvgScored(last3Overall, teamId),
            AvgGoalsConcededLast3 = AvgConceded(last3Overall, teamId),
            BTTSRateLast3 = BTTSRate(last3Overall),
            Over25RateLast3 = Over25Rate(last3Overall),

            // LAST 7 OVERALL
            AvgGoalsScoredLast7 = overallAttack,
            AvgGoalsConcededLast7 = overallConceded,
            BTTSRateLast7 = BTTSRate(last7Overall),
            Over25RateLast7 = Over25Rate(last7Overall),

            // PERFORMANCE (weighted: venue 60% + overall 40%)
            AttackStrength = Math.Round(venueAttack * 0.6 + overallAttack * 0.4, 2),
            DefensiveStrength = Math.Round(venueConceded * 0.6 + overallConceded * 0.4, 2),

            // RESULTS
            CleanSheetRate = CleanSheetRate(last7Overall, teamId),
            ZeroZeroMatches = ZeroZeroMatches(last7Overall),
            ZeroZeroRate = ZeroZeroRate(last7Overall),
            WinRate = WinRate(last7Overall, teamId),
            DrawRate = DrawRate(last7Overall),

            // NEW: Possession and Momentum
            Possession = CalculatePossession(last7Overall, teamId),
            Momentum = CalculateMomentum(last7Overall, teamId)
        };
    }

    private static double CalculatePossession(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 50.0;
        
        var validMatches = matches.Where(m => 
            (m.HomeTeamId == teamId && m.HomeBallPossession.HasValue) || 
            (m.AwayTeamId == teamId && m.AwayBallPossession.HasValue)).ToList();
            
        if (!validMatches.Any()) return 50.0;
        
        return Math.Round(validMatches.Average(m => 
            (m.HomeTeamId == teamId ? m.HomeBallPossession : m.AwayBallPossession) ?? 50.0), 2);
    }

    private static double CalculateMomentum(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;
        
        // weighted points: last 3 are 70%, next 4 are 30%
        var last3 = matches.Take(3).ToList();
        var prev4 = matches.Skip(3).Take(4).ToList();
        
        double points3 = last3.Sum(m => GetPoints(m, teamId));
        double pointsPrev = prev4.Sum(m => GetPoints(m, teamId));
        
        double score3 = (points3 / Math.Max(1, last3.Count * 3)) * 70;
        double scorePrev = prev4.Any() ? (pointsPrev / (double)(prev4.Count * 3)) * 30 : 0;
        
        // Add a small bonus for win streaks
        double streakBonus = 0;
        foreach(var m in last3)
        {
            if (GetPoints(m, teamId) == 3) streakBonus += 5;
            else break;
        }

        // Already capped at 100: score3 (max 70) + scorePrev (max 30) reaches the
        // ceiling on its own, and the streak bonus can only push against a cap
        // that is already there. The range is 0-100, not 0-105.
        return Math.Round(Math.Min(100, score3 + scorePrev + streakBonus), 2);
    }

    private static int GetPoints(Fixture m, int teamId)
    {
        if (m.HomeTeamId == teamId)
        {
            if (m.HomeGoal > m.AwayGoal) return 3;
            if (m.HomeGoal == m.AwayGoal) return 1;
            return 0;
        }
        else
        {
            if (m.AwayGoal > m.HomeGoal) return 3;
            if (m.AwayGoal == m.HomeGoal) return 1;
            return 0;
        }
    }

    private static int GetTeamGoals(Fixture m, int teamId)
        => m.HomeTeamId == teamId ? m.HomeGoal : m.AwayGoal;

    private static double AvgScored(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;
        return Math.Round(matches.Average(m => GetTeamGoals(m, teamId)), 2);
    }
    
    private static double AvgConceded(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;
        return Math.Round(matches.Average(m =>
            m.HomeTeamId == teamId ? m.AwayGoal : m.HomeGoal), 2);
    }
    
    private static double BTTSRate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m => m is { HomeGoal: > 0, AwayGoal: > 0 })
               / (double)matches.Count, 2);
    }

    private static double Over25Rate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m => (m.HomeGoal + m.AwayGoal) >= Config.Over25Threshold)
               / (double)matches.Count, 2);
    }

    private static double CleanSheetRate(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m =>
                   m.HomeTeamId == teamId ? m.AwayGoal == 0 : m.HomeGoal == 0)
               / (double)matches.Count, 2);
    }

    private static int ZeroZeroMatches(List<Fixture> matches)
    {
        return matches.Count(m => m is { HomeGoal: 0, AwayGoal: 0 });
    }

    private static double ZeroZeroRate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m => m is { HomeGoal: 0, AwayGoal: 0 })
               / (double)matches.Count, 2);
    }
    
    private static double WinRate(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m =>
                   (m.HomeTeamId == teamId && m.HomeGoal > m.AwayGoal) ||
                   (m.AwayTeamId == teamId && m.AwayGoal > m.HomeGoal))
               / (double)matches.Count, 2);
    }

    private static double DrawRate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return Math.Round(matches.Count(m => m.HomeGoal == m.AwayGoal)
               / (double)matches.Count, 2);
    }
}