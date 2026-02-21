using soccer_gpt_application.Entities;

namespace soccer_gpt_tests;

public static class TestDataFactory
{
    public static Team CreateTeam(int id, string name) => new()
    {
        Id = id,
        Name = name
    };

    public static Match CreateMatch(
        int id, Team home, Team away, int homeGoals, int awayGoals, 
        DateTime date, string league = "Premier League", bool currentSeason = true)
    {
        var result = homeGoals > awayGoals ? "H" : awayGoals > homeGoals ? "A" : "D";
        return new Match
        {
            Id = id,
            HomeTeam = home,
            HomeTeamId = home.Id,
            AwayTeam = away,
            AwayTeamId = away.Id,
            FullTimeHomeGoal = homeGoals,
            FullTimeAwayGoal = awayGoals,
            FullTimeResult = result,
            HalfTimeHomeGoal = homeGoals > 0 ? 1 : 0,
            HalfTimeAwayGoal = awayGoals > 0 ? 1 : 0,
            HalfTimeResult = "D",
            Date = date,
            Time = new TimeSpan(15, 0, 0),
            LeagueName = league,
            CurrentSeason = currentSeason,
            Referee = "Test Referee"
        };
    }

    public static List<Match> CreateSampleMatches()
    {
        var arsenal = CreateTeam(1, "Arsenal");
        var chelsea = CreateTeam(2, "Chelsea");
        var liverpool = CreateTeam(3, "Liverpool");
        var manCity = CreateTeam(4, "Manchester City");

        var baseDate = new DateTime(2025, 1, 15);

        return
        [
            CreateMatch(1, arsenal, chelsea, 2, 1, baseDate.AddDays(-7)),
            CreateMatch(2, arsenal, liverpool, 1, 1, baseDate.AddDays(-14)),
            CreateMatch(3, arsenal, manCity, 3, 0, baseDate.AddDays(-21)),
            CreateMatch(4, chelsea, arsenal, 0, 2, baseDate.AddDays(-28)),
            CreateMatch(5, liverpool, arsenal, 2, 2, baseDate.AddDays(-35)),
            CreateMatch(6, manCity, arsenal, 1, 0, baseDate.AddDays(-42)),
            CreateMatch(7, chelsea, liverpool, 2, 3, baseDate.AddDays(-7)),
            CreateMatch(8, liverpool, manCity, 1, 1, baseDate.AddDays(-14)),
            CreateMatch(9, manCity, chelsea, 4, 0, baseDate.AddDays(-21))
        ];
    }
}
