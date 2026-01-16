using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public sealed class LeagueStatsService : ILeagueStatsService
{
    public Task<LeagueGoalAverages> CalculateLeagueAveragesAsync(
        string league, IOrderedQueryable<Match> matches)
    {
        var leagueMatches = matches.ToList();
        if (leagueMatches.Count == 0)
            return Task.FromResult(CreateEmptyAverages(league));

        var acc = AggregateGoals(leagueMatches);
        return Task.FromResult(BuildAverages(league, acc));
    }

    private static LeagueGoalAccumulator AggregateGoals(List<Match> matches)
    {
        var acc = new LeagueGoalAccumulator();

        foreach (var m in matches)
        {
            acc.MatchCount++;
            acc.TotalHomeGoals += m.FullTimeHomeGoal;
            acc.TotalAwayGoals += m.FullTimeAwayGoal;
        }

        return acc;
    }

    private static LeagueGoalAverages BuildAverages(
        string league,
        LeagueGoalAccumulator acc)
    {
        return new LeagueGoalAverages
        {
            League = league,
            Season = "Current",
            MatchesPlayed = acc.MatchCount,
            HomeGoalsAvg = SafeDivide(acc.TotalHomeGoals, acc.MatchCount),
            AwayGoalsAvg = SafeDivide(acc.TotalAwayGoals, acc.MatchCount)
        };
    }

    private static LeagueGoalAverages CreateEmptyAverages(string league)
    {
        return new LeagueGoalAverages
        {
            League = league,
            Season = "Current",
            MatchesPlayed = 0,
            HomeGoalsAvg = 0,
            AwayGoalsAvg = 0
        };
    }

    private static double SafeDivide(int numerator, int denominator) 
        => denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 3);
    

    private sealed class LeagueGoalAccumulator
    {
        public int MatchCount;
        public int TotalHomeGoals;
        public int TotalAwayGoals;
    }
}
