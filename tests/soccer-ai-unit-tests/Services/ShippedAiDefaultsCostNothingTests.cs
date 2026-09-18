using System.Text.Json;
using FluentAssertions;
using SoccerAi.Infrastructure.Options;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// A shipped AI model default must be free.
/// </summary>
/// <remarks>
/// The worker carries no model environment variable, so whatever is written
/// here is what it runs. When the default was changed to a paid Anthropic
/// model, that alone spent €10 of OpenRouter credit in a day — 82 fixtures at
/// roughly €0.12 each — with nobody having chosen to spend anything. Paying
/// for a model is a decision an operator makes per service, by setting
/// AISERVICE__DEFAULTMODEL; it is never what happens when no one decided.
/// </remarks>
public class ShippedAiDefaultsCostNothingTests
{
    private static readonly string[] SettingsFiles =
    [
        Path.Combine("src", "soccer-ai-api", "appsettings.json"),
        Path.Combine("src", "soccer-ai-worker", "appsettings.json"),
        Path.Combine("src", "soccer-ai-tools", "appsettings.json")
    ];

    [Fact]
    public void TheCodeDefaultsAreFreeSlugs()
    {
        var options = new AiServiceOptions();

        options.DefaultModel.Should().EndWith(":free");
        options.FallbackModel.Should().EndWith(":free");
    }

    /// <summary>
    /// The primary and the fallback must not share a provider: the fallback
    /// exists for a provider-side outage, and two slugs from one vendor go dark
    /// together.
    /// </summary>
    [Fact]
    public void TheFallbackIsADifferentProvider()
    {
        var options = new AiServiceOptions();

        options.DefaultModel.Split('/')[0]
            .Should().NotBe(options.FallbackModel.Split('/')[0]);
    }

    /// <summary>
    /// Settings files override the code defaults, so a free default in code is
    /// worth nothing if a shipped file names a paid model over the top of it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryShippedSettingsFileNamesFreeModels(int index)
    {
        var path = Path.Combine(RepositoryRoot(), SettingsFiles[index]);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("AiService", out var ai)) return;

        foreach (var key in new[] { "DefaultModel", "FallbackModel" })
        {
            if (!ai.TryGetProperty(key, out var model)) continue;
            model.GetString().Should().EndWith(":free",
                $"{SettingsFiles[index]} ships {key} to every deployment that sets no override");
        }
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
