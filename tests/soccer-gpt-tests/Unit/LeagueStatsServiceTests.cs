using soccer_gpt_application.Services;

namespace soccer_gpt_tests.Unit;

public class LeagueStatsServiceTests
{
    private readonly LeagueStatsService _sut = new();

    [Fact]
    public async Task CalculateLeagueAveragesAsync_WithNoMatches_ReturnsEmptyAverages()
    {
        var matches = TestDataFactory.CreateSampleMatches()
            .Where(m => m.LeagueName == "NonExistent")
            .OrderByDescending(m => m.Date)
            .AsQueryable()
            .OrderByDescending(m => m.Date);

        var result = await _sut.CalculateLeagueAveragesAsync("NonExistent", matches);

        Assert.Equal(0, result.MatchesPlayed);
        Assert.Equal(0, result.HomeGoalsAvg);
        Assert.Equal(0, result.AwayGoalsAvg);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CalculateLeagueAveragesAsync_WithMatches_CalculatesAverages()
    {
        var matches = TestDataFactory.CreateSampleMatches()
            .AsQueryable()
            .OrderByDescending(m => m.Date);

        var result = await _sut.CalculateLeagueAveragesAsync("Premier League", matches);

        Assert.True(result.MatchesPlayed > 0);
        Assert.True(result.HomeGoalsAvg > 0);
        Assert.True(result.AwayGoalsAvg > 0);
    }

    [Fact]
    public async Task CalculateLeagueAveragesAsync_IsValid_WhenEnoughMatches()
    {
        var matches = TestDataFactory.CreateSampleMatches()
            .AsQueryable()
            .OrderByDescending(m => m.Date);

        var result = await _sut.CalculateLeagueAveragesAsync("Premier League", matches);

        // IsValid requires 10+ matches
        Assert.Equal(result.MatchesPlayed >= 10, result.IsValid);
    }

    [Fact]
    public async Task CalculateLeagueAveragesAsync_SetsCorrectLeagueName()
    {
        var matches = TestDataFactory.CreateSampleMatches()
            .AsQueryable()
            .OrderByDescending(m => m.Date);

        var result = await _sut.CalculateLeagueAveragesAsync("Premier League", matches);

        Assert.Equal("Premier League", result.League);
        Assert.Equal("Current", result.Season);
    }
}
