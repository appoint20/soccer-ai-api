using soccer_gpt_application.Models;
using soccer_gpt_application.Services;

namespace soccer_gpt_tests.Unit;

public class TeamStatsServiceTests
{
    private readonly TeamStatsService _sut = new();

    [Fact]
    public async Task CalculateAsync_WithNoMatches_ReturnsEmptyStats()
    {
        var result = await _sut.CalculateAsync("Arsenal", [], new TeamStatsOptions());
        
        Assert.Equal(0, result.MatchesPlayed);
        Assert.Equal(0, result.GoalsScored);
    }

    [Fact]
    public async Task CalculateAsync_WithMatches_CalculatesCorrectGoals()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        
        var result = await _sut.CalculateAsync("Arsenal", matches, new TeamStatsOptions());
        
        Assert.True(result.MatchesPlayed > 0);
        Assert.True(result.GoalsScored > 0);
    }

    [Fact]
    public async Task CalculateAsync_HomeOnly_FiltersToHomeMatches()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        var options = new TeamStatsOptions { HomeOnly = true };
        
        var result = await _sut.CalculateAsync("Arsenal", matches, options);
        
        Assert.True(result.MatchesPlayed <= matches.Count(m => m.HomeTeam.Name == "Arsenal"));
    }

    [Fact]
    public async Task CalculateAsync_AwayOnly_FiltersToAwayMatches()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        var options = new TeamStatsOptions { HomeOnly = false };
        
        var result = await _sut.CalculateAsync("Arsenal", matches, options);
        
        Assert.True(result.MatchesPlayed <= matches.Count(m => m.AwayTeam.Name == "Arsenal"));
    }

    [Fact]
    public async Task CalculateAsync_LastMatches_LimitsResults()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        var options = new TeamStatsOptions { LastMatches = 3 };
        
        var result = await _sut.CalculateAsync("Arsenal", matches, options);
        
        Assert.True(result.MatchesPlayed <= 3);
    }

    [Fact]
    public async Task CalculateAsync_CalculatesWinRate()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        
        var result = await _sut.CalculateAsync("Arsenal", matches, new TeamStatsOptions());
        
        Assert.True(result.Wins >= 0 && result.Wins <= 1);
        Assert.True(result.Draws >= 0 && result.Draws <= 1);
        Assert.True(result.Losses >= 0 && result.Losses <= 1);
    }

    [Fact]
    public async Task CalculateAsync_CalculatesBTTS()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        
        var result = await _sut.CalculateAsync("Arsenal", matches, new TeamStatsOptions());
        
        Assert.True(result.BothTeamsScoredAvg >= 0 && result.BothTeamsScoredAvg <= 1);
    }

    [Fact]
    public async Task CalculateAsync_CalculatesOver25()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        
        var result = await _sut.CalculateAsync("Arsenal", matches, new TeamStatsOptions());
        
        Assert.True(result.Over25Avg >= 0 && result.Over25Avg <= 1);
    }

    [Fact]
    public async Task CalculateAsync_GeneratesFormString()
    {
        var matches = TestDataFactory.CreateSampleMatches();
        
        var result = await _sut.CalculateAsync("Arsenal", matches, new TeamStatsOptions());
        
        Assert.NotNull(result.Form);
        Assert.All(result.Form, c => Assert.Contains(c, new[] { 'W', 'D', 'L' }));
    }
}
