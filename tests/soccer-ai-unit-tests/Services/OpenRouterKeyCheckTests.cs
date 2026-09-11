using FluentAssertions;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The narrative step fails silently on a bad key — every request is rejected,
/// the step completes, the sync reports success. The startup check is the only
/// thing that names the problem, so its judgement is pinned here.
/// </summary>
public class OpenRouterKeyCheckTests
{
    private const string OpenRouter = "https://openrouter.ai/api/v1";

    /// <summary>The production case: a Z.ai-shaped key sent to OpenRouter.</summary>
    [Fact]
    public void AKeyFromAnotherProviderIsFlagged()
    {
        var zaiShaped = new string('a', 32) + "." + new string('b', 16);

        OpenAiAnalysisService.DescribeKeyProblem(OpenRouter, zaiShaped)
            .Should().Contain("not an OpenRouter key");
    }

    [Fact]
    public void AnOpenRouterKeyPasses()
    {
        OpenAiAnalysisService.DescribeKeyProblem(OpenRouter, "sk-or-v1-" + new string('0', 64))
            .Should().BeNull();
    }

    [Fact]
    public void AMissingKeyIsFlagged()
    {
        OpenAiAnalysisService.DescribeKeyProblem(OpenRouter, "  ")
            .Should().Contain("No OpenRouter key");
    }

    /// <summary>An unset base URL means the OpenRouter default, so it is checked.</summary>
    [Fact]
    public void AnUnsetEndpointIsTreatedAsOpenRouter()
    {
        OpenAiAnalysisService.DescribeKeyProblem(null, "not-a-real-key")
            .Should().NotBeNull();
    }

    /// <summary>Pointed at another provider, its own key format is its business.</summary>
    [Fact]
    public void OtherEndpointsAreNotJudged()
    {
        OpenAiAnalysisService.DescribeKeyProblem("https://api.z.ai/api/paas/v4", "abc.def")
            .Should().BeNull();
    }

    [Fact]
    public void TheMessageNeverContainsTheKey()
    {
        const string key = "0123456789abcdef0123456789abcdef.SECRETSECRETSECR";

        OpenAiAnalysisService.DescribeKeyProblem(OpenRouter, key)
            .Should().NotContain("SECRET").And.NotContain("0123456789abcdef");
    }
}
