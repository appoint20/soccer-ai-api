using FluentAssertions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class LeagueTierServiceTests
{
    private static LeagueTierService CreateSut(bool includeTier2 = false, double boost = 10.0) =>
        new(Microsoft.Extensions.Options.Options.Create(new LeagueTierOptions
        {
            IncludeTier2 = includeTier2,
            Tier2QualificationThresholdBoost = boost
        }));

    [Theory]
    [InlineData(39)]   // Premier League
    [InlineData(40)]   // Championship
    [InlineData(41)]   // League One
    [InlineData(42)]   // League Two
    [InlineData(46)]   // National League
    [InlineData(78)]   // Bundesliga
    [InlineData(79)]   // 2. Bundesliga
    [InlineData(80)]   // 3. Liga
    [InlineData(140)]  // La Liga
    [InlineData(141)]  // La Liga 2
    [InlineData(135)]  // Serie A
    [InlineData(136)]  // Serie B
    [InlineData(61)]   // Ligue 1
    [InlineData(62)]   // Ligue 2
    public void Tier1Leagues_AreAlwaysInScope(int leagueId)
    {
        var sut = CreateSut(includeTier2: false);

        sut.GetTier(leagueId).Should().Be(LeagueTier.Tier1);
        sut.IsInScope(leagueId).Should().BeTrue();
        sut.GetQualificationThresholdBoost(leagueId).Should().Be(0);
    }

    [Theory]
    [InlineData(2)]    // Champions League
    [InlineData(3)]    // Europa League
    [InlineData(848)]  // Conference League
    public void Tier2Leagues_OnlyInScopeWithFlag(int leagueId)
    {
        var disabled = CreateSut(includeTier2: false);
        disabled.GetTier(leagueId).Should().Be(LeagueTier.Tier2);
        disabled.IsInScope(leagueId).Should().BeFalse();

        var enabled = CreateSut(includeTier2: true);
        enabled.IsInScope(leagueId).Should().BeTrue();
    }

    [Fact]
    public void Tier2_GetsStricterQualificationThreshold()
    {
        var sut = CreateSut(boost: 12.5);

        sut.GetQualificationThresholdBoost(2).Should().Be(12.5, "Champions League is Tier2");
        sut.GetQualificationThresholdBoost(39).Should().Be(0, "Premier League is Tier1");
        sut.GetQualificationThresholdBoost(999).Should().Be(0, "unknown leagues get no boost");
    }

    [Fact]
    public void UnknownLeague_NeverInScope()
    {
        var sut = CreateSut(includeTier2: true);

        sut.GetTier(999).Should().Be(LeagueTier.Unknown);
        sut.IsInScope(999).Should().BeFalse();
    }

    [Fact]
    public void SyncLeagueIds_Tier1Only_ByDefault()
    {
        var ids = CreateSut(includeTier2: false).GetSyncLeagueIds();

        ids.Should().Contain(39).And.Contain(62);
        ids.Should().NotContain(2).And.NotContain(3).And.NotContain(848);
    }

    [Fact]
    public void SyncLeagueIds_IncludeTier2_AppendsCups()
    {
        var ids = CreateSut(includeTier2: true).GetSyncLeagueIds();

        ids.Should().Contain([2, 3, 848]);
        ids.Should().Contain(39);
    }

    [Fact]
    public void SyncLeagueIds_AreDeduplicated()
    {
        // .NET configuration binding appends to an array's default rather than
        // replacing it, so a Tier1 list in appsettings arrives doubled. Callers
        // that loop over this — the fixture sync — would spend twice the API
        // quota, and no membership check would ever reveal it.
        var options = new LeagueTierOptions { Tier1 = [39, 78, 39, 78], IncludeTier2 = false };
        var sut = new LeagueTierService(Microsoft.Extensions.Options.Options.Create(options));

        sut.GetSyncLeagueIds().Should().Equal(39, 78);
    }
}
