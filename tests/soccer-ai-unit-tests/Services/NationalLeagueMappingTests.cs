using System.Text.Json;
using FluentAssertions;
using SoccerAi.Application.Options;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The National League is API-Football league 43. It was once configured as 46
/// — the EFL Trophy, a cup — plus a placeholder 5, and removing those stopped
/// the real league syncing for weeks without anything reporting a failure.
/// </summary>
public class NationalLeagueMappingTests
{
    private const int NationalLeague = 43;
    private const int EflTrophy = 46;
    private const int RetiredPlaceholder = 5;

    [Fact]
    public void TheNationalLeagueIsSyncedByDefault()
    {
        var tier1 = new LeagueTierOptions().Tier1;

        tier1.Should().Contain(NationalLeague);
        tier1.Should().NotContain(new[] { EflTrophy, RetiredPlaceholder });
    }

    [Fact]
    public void ItsHistoricalOddsDivisionIsKeyedToTheRealId()
    {
        var divisions = new HistoricalOddsOptions().Divisions;

        divisions.Should().Contain(NationalLeague, "EC");
        divisions.Should().NotContainKey(EflTrophy);
    }

    [Fact]
    public void ItsTableZonesBelongToTheLeagueNotTheCup()
    {
        var strategy = new StrategyOptions();
        var profile = strategy.GetProfile(NationalLeague);

        profile.LeagueSize.Should().Be(24);
        profile.RelegationSpots.Should().Be(4);
        profile.PlayoffStart.Should().Be(2);
        profile.PlayoffEnd.Should().Be(7);
        strategy.LeagueProfiles.Should().NotContainKey(EflTrophy);
    }

    /// <summary>
    /// Settings files add to the defaults above rather than replacing them, so
    /// an id fixed in code survives in a stale file. The tools settings still
    /// synced 46 and 5 long after the code had dropped them.
    /// </summary>
    [Theory]
    [InlineData("src/soccer-ai-api/appsettings.json")]
    [InlineData("src/soccer-ai-worker/appsettings.json")]
    [InlineData("src/soccer-ai-tools/appsettings.json")]
    public void EveryShippedSettingsFileAgrees(string relativePath)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;

        var tier1 = root.GetProperty("LeagueTiers").GetProperty("Tier1")
            .EnumerateArray().Select(e => e.GetInt32()).ToList();
        tier1.Should().Contain(NationalLeague);
        tier1.Should().NotContain(new[] { EflTrophy, RetiredPlaceholder });

        var divisions = root.GetProperty("HistoricalOdds").GetProperty("Divisions");
        divisions.GetProperty(NationalLeague.ToString()).GetString().Should().Be("EC");
        divisions.TryGetProperty(EflTrophy.ToString(), out _).Should().BeFalse();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "soccer-ai-api", "appsettings.json")))
            dir = dir.Parent;

        return dir?.FullName
               ?? throw new InvalidOperationException($"Repository root not found above {AppContext.BaseDirectory}");
    }
}
