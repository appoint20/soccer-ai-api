using FluentAssertions;
using SoccerAi.Application.Services.Odds;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// A wrong team match writes one club's prices onto another club's fixture.
/// Nothing downstream would ever flag it, and the backtest would be quietly
/// wrong — so these tests care as much about refusing bad matches as accepting
/// good ones.
/// </summary>
public class TeamNameMatcherTests
{
    private const double Threshold = 0.85;

    private static readonly TeamCandidate[] English =
    [
        new(1, "Manchester United", "Man Utd"),
        new(2, "Manchester City", "Man City"),
        new(3, "Nottingham Forest", null),
        new(4, "Wolverhampton Wanderers", "Wolves"),
        new(5, "Tottenham Hotspur", "Spurs"),
        new(6, "Brighton & Hove Albion", null),
        new(7, "Sheffield United", null),
        new(8, "Sheffield Wednesday", null)
    ];

    private static TeamNameMatch? Match(string name) =>
        TeamNameMatcher.Match(name, English, Threshold);

    // ── Normalization ────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_StripsAccents() =>
        TeamNameMatcher.Normalize("Atlético Madrid").Should().Be("atletico madrid");

    [Fact]
    public void Normalize_StripsPunctuation() =>
        TeamNameMatcher.Normalize("Brighton & Hove Albion").Should().Be("brighton hove albion");

    [Fact]
    public void Normalize_DropsClubTypeWords() =>
        TeamNameMatcher.Normalize("FC Bayern München").Should().Be("bayern munchen");

    [Fact]
    public void Normalize_KeepsDigits()
    {
        // Schalke 04 and Hannover 96 are distinguished by their numbers.
        TeamNameMatcher.Normalize("FC Schalke 04").Should().Be("schalke 04");
    }

    [Fact]
    public void Normalize_NameOfOnlyClubWords_DoesNotCollapseToNothing() =>
        TeamNameMatcher.Normalize("FC").Should().NotBeEmpty();

    [Theory]
    [InlineData("Nott'm Forest", "nottm forest")]
    [InlineData("King's Lynn", "kings lynn")]
    [InlineData("M’Gladbach", "mgladbach")]
    [InlineData("Kings Lynn", "kings lynn")]
    public void Normalize_TreatsApostrophesAsJoiners(string raw, string expected)
    {
        // Splitting on an apostrophe leaves a one-letter token that matches
        // nothing. U+0092 is the Windows-1252 curly quote as it appears once
        // these Latin-1 files are read.
        TeamNameMatcher.Normalize(raw).Should().Be(expected);
    }

    // ── Matching ─────────────────────────────────────────────────────────────

    [Fact]
    public void ExactName_Matches()
    {
        var match = Match("Manchester United");

        match.Should().NotBeNull();
        match!.ApiId.Should().Be(1);
        match.Method.Should().Be(TeamNameMatch.Exact);
    }

    [Fact]
    public void ShortName_Matches() =>
        Match("Man Utd")!.ApiId.Should().Be(1);

    [Theory]
    [InlineData("Man United", 1)]
    [InlineData("Man City", 2)]
    [InlineData("Nott'm Forest", 3)]
    [InlineData("Wolves", 4)]
    [InlineData("Tottenham", 5)]
    [InlineData("Brighton", 6)]
    public void KnownFeedSpellings_Match(string csvName, int expectedApiId) =>
        Match(csvName)!.ApiId.Should().Be(expectedApiId);

    [Fact]
    public void AbbreviatedTokens_MatchByPrefix()
    {
        // "Wolverhampton Wanderers" abbreviated: edit distance handles this
        // badly, token prefixes handle it well.
        var match = TeamNameMatcher.Match(
            "Wolverhampton", [new TeamCandidate(4, "Wolverhampton Wanderers", null)], Threshold);

        match.Should().NotBeNull();
        match!.Method.Should().Be(TeamNameMatch.TokenPrefix);
    }

    // ── Refusals: the important half ─────────────────────────────────────────

    [Fact]
    public void UnknownTeam_ReturnsNullRatherThanTheNearestName() =>
        Match("Real Madrid").Should().BeNull();

    [Fact]
    public void SheffieldClubs_AreNotConfused()
    {
        // "Sheffield United" and "Sheffield Wednesday" share a token and are
        // close in spelling. Getting this wrong would swap two clubs' results.
        Match("Sheffield United")!.ApiId.Should().Be(7);
        Match("Sheffield Weds")!.ApiId.Should().Be(8);
    }

    [Fact]
    public void ManchesterClubs_AreNotConfused()
    {
        Match("Man United")!.ApiId.Should().Be(1);
        Match("Man City")!.ApiId.Should().Be(2);
    }

    [Fact]
    public void SingleLetterToken_IsNotEvidence()
    {
        // "M United" must not sail through on a one-character prefix.
        TeamNameMatcher.Match("M United", [new TeamCandidate(1, "Manchester United", null)], 0.99)
            .Should().BeNull();
    }

    [Fact]
    public void EmptyName_MatchesNothing() =>
        Match("   ").Should().BeNull();

    [Fact]
    public void NoCandidates_MatchesNothing() =>
        TeamNameMatcher.Match("Manchester United", [], Threshold).Should().BeNull();

    [Fact]
    public void RaisingTheThreshold_RejectsWeakMatches()
    {
        var loose = TeamNameMatcher.Match("Manchestr Utd", English, 0.70);
        var strict = TeamNameMatcher.Match("Manchestr Utd", English, 0.99);

        loose.Should().NotBeNull();
        strict.Should().BeNull();
    }
}
