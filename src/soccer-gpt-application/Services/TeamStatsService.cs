using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public interface ITeamStatsService
{
    TeamStats Calculate(int teamId, IEnumerable<Fixture> allMatches, bool isHomeTeam);
}

public class TeamStatsService: ITeamStatsService
{
    private static class Config
    {
        public const int OverallWindow = 7;
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

        var venueMatches = ordered
            .Where(m =>
                isHomeTeam ? m.HomeTeamId == teamId : m.AwayTeamId == teamId)
            .Take(Config.VenueWindow)
            .ToList();

        var venueAttack = AvgScored(venueMatches, teamId);
        var overallAttack = AvgScored(last7Overall, teamId);
        var venueConceded = AvgConceded(venueMatches, teamId);
        var overallConceded = AvgConceded(last7Overall, teamId);

        return new TeamStats
        {
            // LAST 3 VENUE
            AvgGoalsScoredLast3 = venueAttack,
            AvgGoalsConcededLast3 = venueConceded,
            BTTSRateLast3 = BTTSRate(venueMatches),
            Over25RateLast3 = Over25Rate(venueMatches),

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
            ZeroZeroRate = ZeroZeroRate(last7Overall),
            WinRate = WinRate(last7Overall, teamId),
            DrawRate = DrawRate(last7Overall)
        };
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

        return matches.Count(m => m is { HomeGoal: > 0, AwayGoal: > 0 })
               / (double)matches.Count;
    }

    private static double Over25Rate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return matches.Count(m => (m.HomeGoal + m.AwayGoal) >= Config.Over25Threshold)
               / (double)matches.Count;
    }

    private static double CleanSheetRate(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;

        return matches.Count(m =>
                   m.HomeTeamId == teamId ? m.AwayGoal == 0 : m.HomeGoal == 0)
               / (double)matches.Count;
    }

    private static double ZeroZeroRate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return matches.Count(m => m is { HomeGoal: 0, AwayGoal: 0 })
               / (double)matches.Count;
    }
    
    private static double WinRate(List<Fixture> matches, int teamId)
    {
        if (!matches.Any()) return 0;

        return matches.Count(m =>
                   (m.HomeTeamId == teamId && m.HomeGoal > m.AwayGoal) ||
                   (m.AwayTeamId == teamId && m.AwayGoal > m.HomeGoal))
               / (double)matches.Count;
    }

    private static double DrawRate(List<Fixture> matches)
    {
        if (!matches.Any()) return 0;

        return matches.Count(m => m.HomeGoal == m.AwayGoal)
               / (double)matches.Count;
    }
}