using FluentAssertions;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// A rejected narrative request is only ever seen through the message the sync
/// persists, so that message has to carry what the provider actually said.
/// Production spent a day on "OpenRouter rate limit exceeded", which is true of
/// both a burst that clears in a minute and a free-tier day that is simply gone.
/// </summary>
public class OpenRouterRejectionTests
{
    [Fact]
    public void AFreeSlugExplainsTheDailyCap()
    {
        var reason = OpenAiAnalysisService.RateLimitReason("z-ai/glm-5.2:free");

        reason.Should().Contain("free tier");
        reason.Should().Contain("50 per day");
        reason.Should().Contain("one request per fixture");
    }

    /// <summary>A paid slug has no daily cap, so the advice must not appear.</summary>
    [Fact]
    public void APaidSlugDoesNotMentionTheFreeCap()
    {
        var reason = OpenAiAnalysisService.RateLimitReason("anthropic/claude-sonnet-5");

        reason.Should().NotContain("free");
        reason.Should().Contain("retry on the next sync");
    }

    [Fact]
    public void TheProviderMessageIsLifted()
    {
        const string body = """
            {"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day",
             "metadata":{"error_type":"rate_limit_exceeded"}}}
            """;

        OpenAiAnalysisService.DescribeProviderError(body)
            .Should().Be("Rate limit exceeded: free-models-per-day");
    }

    /// <summary>Some gateways answer with a bare string in the same field.</summary>
    [Fact]
    public void AStringErrorFieldIsLifted()
    {
        OpenAiAnalysisService.DescribeProviderError("""{"error":"No auth credentials found"}""")
            .Should().Be("No auth credentials found");
    }

    /// <summary>A proxy in front of the provider answers with HTML, not JSON.</summary>
    [Fact]
    public void AnUnparseableBodyIsKeptVerbatim()
    {
        OpenAiAnalysisService.DescribeProviderError("<html>502 Bad Gateway</html>")
            .Should().Be("<html>502 Bad Gateway</html>");
    }

    [Fact]
    public void AnEmptyBodyDescribesNothing()
    {
        OpenAiAnalysisService.DescribeProviderError("   ").Should().BeNull();
        OpenAiAnalysisService.DescribeProviderError((string?)null).Should().BeNull();
    }

    /// <summary>
    /// The message reaches the sync status table and the logs, and both are read
    /// in public. A provider that echoes the key back must not be relayed.
    /// </summary>
    [Fact]
    public void AKeyInTheProviderMessageIsRedacted()
    {
        var body = """{"error":{"message":"Invalid key sk-or-v1-"""
                   + new string('a', 64) + """ supplied"}}""";

        var described = OpenAiAnalysisService.DescribeProviderError(body);

        described.Should().NotContain("aaaa");
        described.Should().Contain("sk-***");
    }

    [Fact]
    public void ALongBodyIsTruncated()
    {
        var described = OpenAiAnalysisService.DescribeProviderError(new string('x', 900));

        described!.Length.Should().BeLessThan(320);
        described.Should().EndWith("…");
    }
}
