using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class ApiQuotaTrackerTests
{
    private static ApiQuotaTracker CreateSut() =>
        new(new Mock<ILogger<ApiQuotaTracker>>().Object);

    private static Func<string, string?> Headers(
        string? dailyLimit = null, string? dailyRemaining = null,
        string? minuteLimit = null, string? minuteRemaining = null) =>
        name => name switch
        {
            "x-ratelimit-requests-limit" => dailyLimit,
            "x-ratelimit-requests-remaining" => dailyRemaining,
            "X-RateLimit-Limit" => minuteLimit,
            "X-RateLimit-Remaining" => minuteRemaining,
            _ => null
        };

    [Fact]
    public void ReadsAllFourHeaders()
    {
        var sut = CreateSut();

        sut.Update(Headers("7500", "7000", "300", "250"));

        sut.Current.DailyLimit.Should().Be(7500);
        sut.Current.DailyRemaining.Should().Be(7000);
        sut.Current.MinuteLimit.Should().Be(300);
        sut.Current.MinuteRemaining.Should().Be(250);
        sut.Current.DailyUsedShare.Should().BeApproximately(0.0667, 0.001);
    }

    [Fact]
    public void MissingHeaders_KeepLastKnownState()
    {
        var sut = CreateSut();
        sut.Update(Headers("7500", "7000", "300", "250"));

        sut.Update(Headers()); // e.g. an error response with no quota headers

        sut.Current.DailyRemaining.Should().Be(7000, "last known state is preserved");
    }

    [Theory]
    [InlineData(7500, 7000, false)]  // 93% left
    [InlineData(7500, 800, false)]   // 10.7% left — just above the line
    [InlineData(7500, 750, true)]    // exactly 10% left
    [InlineData(7500, 100, true)]    // nearly exhausted
    public void DailyCritical_AtOrBelowTenPercentRemaining(int limit, int remaining, bool expected)
    {
        var sut = CreateSut();
        sut.Update(Headers(limit.ToString(), remaining.ToString()));

        sut.IsDailyQuotaCritical.Should().Be(expected);
    }

    [Fact]
    public void UnknownQuota_IsNotCritical()
    {
        CreateSut().IsDailyQuotaCritical.Should().BeFalse("no data must never block syncing");
    }

    [Theory]
    [InlineData(300, 290, 100)]    // plenty left → minimal spacing
    [InlineData(300, 120, 500)]    // 40% left → slow a bit
    [InlineData(300, 45, 2000)]    // 15% left → 2s
    [InlineData(300, 10, 10000)]   // 3% left → wait for the window
    public void SuggestedDelay_GrowsAsMinuteBudgetShrinks(int limit, int remaining, int expectedMs)
    {
        var sut = CreateSut();
        sut.Update(Headers(minuteLimit: limit.ToString(), minuteRemaining: remaining.ToString()));

        sut.SuggestedDelay.Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void SuggestedDelay_UnknownQuota_UsesConservativeDefault()
    {
        CreateSut().SuggestedDelay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void GarbageHeaderValues_AreIgnored()
    {
        var sut = CreateSut();
        sut.Update(Headers("7500", "7000"));

        sut.Update(Headers("not-a-number", "also-bad"));

        sut.Current.DailyRemaining.Should().Be(7000);
    }
}
