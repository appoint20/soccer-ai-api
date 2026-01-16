using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public sealed class TeamStatsService : ITeamStatsService
{
    public Task<TeamAggregatedStats> CalculateAsync(string team, List<Match> matches, TeamStatsOptions options)
    {
        var filtered = ApplyFilters(team, matches, options);

        if (filtered.Count == 0)
            return Task.FromResult(new TeamAggregatedStats());

        var acc = Aggregate(team, filtered);
        return Task.FromResult(BuildStats(acc, filtered, team));
    }

    private static List<Match> ApplyFilters(
        string team,
        List<Match> matches,
        TeamStatsOptions options)
    {
        var query = matches;
        
        if (options.HomeOnly.HasValue)
        {
            query = options.HomeOnly.Value
                ? query.Where(m => IsMatch(m.HomeTeam.Name, team)).ToList()
                : query.Where(m => IsMatch(m.AwayTeam.Name, team)).ToList();
        }

        return options.LastMatches == 0 ? query : query.Take(options.LastMatches).ToList();
    }

    private static TeamStatAccumulator Aggregate(string team, IReadOnlyList<Match> matches)
    {
        var acc = new TeamStatAccumulator();

        foreach (var m in matches)
        {
            var isHome = IsMatch(m.HomeTeam.Name, team);
            var gf = isHome ? m.FullTimeHomeGoal : m.FullTimeAwayGoal;
            var ga = isHome ? m.FullTimeAwayGoal : m.FullTimeHomeGoal;

            acc.Played++;
            acc.GoalsFor += gf;
            acc.GoalsAgainst += ga;

            if (gf == ga) acc.Draws++;
            if (gf > 0 && ga > 0) acc.BTTS++;
            if (gf + ga > 2) acc.Over25++;
            if (gf + ga is 2 or 3) acc.Goals23++;
            if (ga == 0) acc.CleanSheets++;
            if (gf == 0) acc.FailedToScore++;

            if ((isHome && m.FullTimeResult == "H") || (!isHome && m.FullTimeResult == "A"))
                acc.Wins++;
        }

        return acc;
    }

    private static TeamAggregatedStats BuildStats(
        TeamStatAccumulator a,
        IEnumerable<Match> matches,
        string team)
    {
        return new TeamAggregatedStats
        {
            MatchesPlayed = a.Played,

            GoalsScored = a.GoalsFor,
            GoalsConceded = a.GoalsAgainst,

            GoalsScoredAvg = SafeDivide(a.GoalsFor, a.Played),
            GoalsConcededAvg = SafeDivide(a.GoalsAgainst, a.Played),

            Wins = SafeDivide(a.Wins, a.Played),
            Draws = SafeDivide(a.Draws, a.Played),
            Losses = SafeDivide(a.Played - a.Wins - a.Draws, a.Played),

            BothTeamsScoredAvg = SafeDivide(a.BTTS, a.Played),
            Over25Avg = SafeDivide(a.Over25, a.Played),
            TwoToThreeGoalsAvg = SafeDivide(a.Goals23, a.Played),

            CleanSheetAvg = SafeDivide(a.CleanSheets, a.Played),
            FailedToScoreAvg = SafeDivide(a.FailedToScore, a.Played),

            Form = BuildForm(matches, team)
        };
    }

    private static string BuildForm(IEnumerable<Match> matches, string team)
    {
        var form = "";

        foreach (var m in matches)
        {
            if (m.FullTimeResult == "D")
            {
                form += "D";
                continue;
            }

            bool isHome = IsMatch(m.HomeTeam.Name, team);
            form += (isHome && m.FullTimeResult == "H") || (!isHome && m.FullTimeResult == "A")
                ? "W"
                : "L";
        }

        return form;
    }

    private static bool IsMatch(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        return a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static double SafeDivide(int value, int total)
        => total == 0 ? 0 : Math.Round((double)value / total, 2);

    private sealed class TeamStatAccumulator
    {
        public int Played;
        public int GoalsFor;
        public int GoalsAgainst;
        public int Wins;
        public int Draws;
        public int BTTS;
        public int Over25;
        public int Goals23;
        public int CleanSheets;
        public int FailedToScore;
    }
}
