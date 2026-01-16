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
        var homeStats = CreateValidStats(2.0, 0.8, 2); // Less than minimum
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
        var strengthFactors = new StrengthFactors
        {
            HomeAttackStrength = 1.2,
            HomeDefenseStrength = 0.9,
            AwayAttackStrength = 1.1,
            AwayDefenseStrength = 1.0,
            HomeExpectedGoals = 1.8,
            AwayExpectedGoals = 1.2
        };

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.HomeWin > 0 && result.HomeWin < 1);
        Assert.True(result.Draw > 0 && result.Draw < 1);
        Assert.True(result.AwayWin > 0 && result.AwayWin < 1);
    }

    [Fact]
    public void CalculateProbabilities_1X2_SumToOne()
    {
        var strengthFactors = new StrengthFactors
        {
            HomeAttackStrength = 1.2,
            HomeDefenseStrength = 0.9,
            AwayAttackStrength = 1.1,
            AwayDefenseStrength = 1.0,
            HomeExpectedGoals = 1.8,
            AwayExpectedGoals = 1.2
        };

        var result = _sut.CalculateProbabilities(strengthFactors);
        var sum = result.HomeWin + result.Draw + result.AwayWin;

        Assert.True(sum > 0.95 && sum < 1.05); // Allow small rounding errors
    }

    [Fact]
    public void CalculateProbabilities_Over25_IsReasonable()
    {
        var strengthFactors = new StrengthFactors
        {
            HomeExpectedGoals = 2.5,
            AwayExpectedGoals = 1.5,
            HomeAttackStrength = 1.5,
            HomeDefenseStrength = 1.0,
            AwayAttackStrength = 1.0,
            AwayDefenseStrength = 1.2
        };

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.True(result.Over25 > 0.5); // High expected goals = high over 2.5
        Assert.Equal(1, Math.Round(result.Over25 + result.Under25, 2));
    }

    [Fact]
    public void CalculateProbabilities_TopScores_HasFiveEntries()
    {
        var strengthFactors = new StrengthFactors
        {
            HomeExpectedGoals = 1.5,
            AwayExpectedGoals = 1.2,
            HomeAttackStrength = 1.0,
            HomeDefenseStrength = 1.0,
            AwayAttackStrength = 1.0,
            AwayDefenseStrength = 1.0
        };

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.Equal(5, result.TopScores.Count);
        Assert.True(result.TopScores[0].Probability >= result.TopScores[1].Probability);
    }

    [Fact]
    public void CalculateProbabilities_MostLikelyScore_IsValid()
    {
        var strengthFactors = new StrengthFactors
        {
            HomeExpectedGoals = 1.5,
            AwayExpectedGoals = 1.0,
            HomeAttackStrength = 1.0,
            HomeDefenseStrength = 1.0,
            AwayAttackStrength = 1.0,
            AwayDefenseStrength = 1.0
        };

        var result = _sut.CalculateProbabilities(strengthFactors);

        Assert.NotEmpty(result.MostLikelyScore);
        Assert.Contains(":", result.MostLikelyScore);
    }

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
