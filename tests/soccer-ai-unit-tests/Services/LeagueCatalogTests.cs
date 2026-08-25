using FluentAssertions;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class LeagueCatalogTests
{
    [Theory]
    [InlineData(39, "Premier League")]
    [InlineData(41, "League One")]
    [InlineData(80, "3. Liga")]
    [InlineData(848, "Conference League")]
    public void KnownIds_CarryTheirName(int id, string expected) =>
        LeagueCatalog.Name(id).Should().Be(expected);

    [Fact]
    public void UnknownId_IsLabelledAsUnknown_NotGuessed()
    {
        // A wrong league name is worse than a visibly missing one: the board
        // groups by name, so a guess quietly files fixtures under a real league.
        LeagueCatalog.Name(9999).Should().Be("League 9999");
        LeagueCatalog.IsKnown(9999).Should().BeFalse();
    }

    [Fact]
    public void NoTwoIdsShareAName()
    {
        // Four ids once shared "National League", which merged unrelated
        // competitions into one section on the board.
        var named = Enumerable.Range(1, 1000)
            .Where(LeagueCatalog.IsKnown)
            .Select(LeagueCatalog.Name)
            .ToList();

        named.Should().OnlyHaveUniqueItems();
    }
}
