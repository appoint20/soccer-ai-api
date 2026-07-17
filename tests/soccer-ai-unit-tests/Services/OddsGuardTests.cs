using FluentAssertions;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class OddsGuardTests
{
    [Theory]
    [InlineData(1.01)]
    [InlineData(1.85)]
    [InlineData(2.50)]
    [InlineData(15.0)]
    public void PlausibleDecimalOdds_AreValid(double odds) =>
        OddsGuard.IsValid(odds).Should().BeTrue();

    [Theory]
    [InlineData(185)]    // "1.85" parsed under de-DE locale
    [InlineData(49.0)]   // corrupted average seen in baseline-v2
    [InlineData(92.0)]
    [InlineData(15.01)]
    [InlineData(1.0)]    // no-payout odds
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-2)]
    public void ImplausibleOdds_AreInvalid(double odds) =>
        OddsGuard.IsValid(odds).Should().BeFalse();

    [Fact]
    public void Null_IsInvalid() => OddsGuard.IsValid(null).Should().BeFalse();

    [Fact]
    public void Sanitize_NeverClampsOrSubstitutes()
    {
        OddsGuard.Sanitize(185).Should().BeNull("corrupted odds are excluded, not rescaled");
        OddsGuard.Sanitize(1.85).Should().Be(1.85);
        OddsGuard.Sanitize(null).Should().BeNull();
    }
}
