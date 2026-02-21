using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_tests.Unit;

public class PoissonServiceTests
{
    private readonly PoissonService _sut = new();

    [Fact]
    public void Build_WithValidInput_ReturnsStrengthFactors()
    {
        var homeStats = CreateValidStats(2.0, 0.8, 10);
        var awayStats = CreateValidStats(1.5, 1.2, 10);
        var leagueAvg = CreateValidLeagueAverages();

        var result = _sut.Build(homeStats, awayStats, leagueAvg);

        Assert.True(result.HomeAttackStrength > 0);
        Assert.True(result.HomeDefenseStrength > 0);
        Assert.True(result.AwayAttackStrength > 0);
        Assert.True(result.AwayDefenseStrength > 0);
    }

    [Fact]
    public void Build_WithInsufficientSamples_ThrowsException()
    {
        var homeStats = CreateValidStats(2.0, 0.8, 2);
        var awayStats = CreateValidStats(1.5, 1.2, 10);
        var leagueAvg = CreateValidLeagueAverages();

        Assert.Throws<Exception>(() => _sut.Build(homeStats, awayStats, leagueAvg));
    }

    [Fact]
    public void Build_CalculatesExpectedGoals()
    {
        var homeStats = CreateValidStats(2.0, 0.8, 10);
        var awayStats = CreateValidStats(1.5, 1.2, 10);
        var leagueAvg = CreateValidLeagueAverages();

        var result = _sut.Build(homeStats, awayStats, leagueAvg);

        Assert.True(result.HomeExpectedGoals > 0);
        Assert.True(result.AwayExpectedGoals > 0);
    }

    [Fact]
    public void CalculateProbabilities_ReturnsValidProbabilities()
    {
        var strengthFactors = CreateStrengthFactors(1.8, 1.2);

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.HomeWin > 0 && result.HomeWin < 1);
        Assert.True(result.Draw > 0 && result.Draw < 1);
        Assert.True(result.AwayWin > 0 && result.AwayWin < 1);
    }

    [Fact]
    public void CalculateProbabilities_1X2_SumToOne()
    {
        var strengthFactors = CreateStrengthFactors(1.8, 1.2);

        var result = _sut.CalculateProbabilities(strengthFactors);
        var sum = result.HomeWin + result.Draw + result.AwayWin;

        Assert.True(sum > 0.95 && sum < 1.05);
    }

    [Fact]
    public void CalculateProbabilities_Over25_IsReasonable()
    {
        var strengthFactors = CreateStrengthFactors(2.5, 1.5);

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.Over25 > 0.5);
        Assert.Equal(1, Math.Round(result.Over25 + result.Under25, 2));
    }

    [Fact]
    public void CalculateProbabilities_BTTS_IsReasonable()
    {
        var strengthFactors = CreateStrengthFactors(1.5, 1.5);

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.BothTeamScoredGoal > 0 && result.BothTeamScoredGoal < 1);
        Assert.Equal(1, Math.Round(result.BothTeamScoredGoal + result.BTTSNo, 2));
    }

    [Fact]
    public void CalculateProbabilities_TwoToThreeGoals_IsPositive()
    {
        var strengthFactors = CreateStrengthFactors(1.2, 1.0);

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.TwoToThreeGoals > 0);
    }

    private static StrengthFactors CreateStrengthFactors(double homeXg, double awayXg) => new()
    {
        HomeAttackStrength = 1.0,
        HomeDefenseStrength = 1.0,
        AwayAttackStrength = 1.0,
        AwayDefenseStrength = 1.0,
        HomeExpectedGoals = homeXg,
        AwayExpectedGoals = awayXg
    };

    private static TeamAggregatedStats CreateValidStats(double goalsScored, double goalsConceded, int matches) => new()
    {
        MatchesPlayed = matches,
        GoalsScored = (int)(goalsScored * matches),
        GoalsConceded = (int)(goalsConceded * matches),
        GoalsScoredAvg = goalsScored,
        GoalsConcededAvg = goalsConceded
    };

    private static LeagueGoalAverages CreateValidLeagueAverages() => new()
    {
        League = "Premier League",
        Season = "Current",
        MatchesPlayed = 100,
        HomeGoalsAvg = 1.5,
        AwayGoalsAvg = 1.1
    };
}
