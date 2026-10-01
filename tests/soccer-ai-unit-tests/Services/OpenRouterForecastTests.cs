using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class OpenRouterForecastTests
{
    [Theory]
    [InlineData(.65, true)]
    [InlineData(65, false)]
    [InlineData(-.1, false)]
    public async Task ValidForecastUsesProviderContextAndInvalidProbabilityIsRejected(double probability, bool valid)
    {
        using var handler = new Reply(probability);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(OpenRouterForecastService.HttpClientName)).Returns(client);
        var service = new OpenRouterForecastService(factory.Object, Options.Create(new OpenRouterOptions
        {
            ApiKey = "local-test-key", Models = ["nvidia/nemotron-3-super-120b-a12b:free"]
        }), NullLogger<OpenRouterForecastService>.Instance);
        var result = await service.ForecastAsync(new MatchAnalysis
        {
            Id = 123, Provider = new ProviderPrediction { PercentHome = .45, UnderOver = "-3.5" }
        });
        result.Should().HaveCount(valid ? 1 : 0);
        using var request = JsonDocument.Parse(handler.Request!);
        request.RootElement.GetProperty("reasoning").GetProperty("enabled").GetBoolean().Should().BeFalse();
        var prompt = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        prompt.Should().Contain("API-FOOTBALL ASSESSMENT").And.Contain("\"PercentHome\":0.45");
        if (valid) result[0].Over25Probability.Should().Be(probability);
    }

    [Theory]
    [InlineData(.65, 2.8, true)]
    [InlineData(65, 2.8, false)]
    [InlineData(.65, .39, false)]
    public async Task CombinedForecastRequiresAllMarketsAndCoherentExpectedGoals(double probability, double expectedGoals, bool valid)
    {
        using var handler = new Reply(probability, full: true, expectedGoals);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(OpenRouterForecastService.HttpClientName)).Returns(client);
        var service = new OpenRouterForecastService(factory.Object, Options.Create(new OpenRouterOptions { ApiKey = "sk-or-local-test-key" }),
            NullLogger<OpenRouterForecastService>.Instance);
        var result = await service.ForecastCombinedAsync(new MatchAnalysis { Id = 123 }, "nvidia/test:free");
        (result is not null).Should().Be(valid);
        if (valid)
        {
            result!.Markets!.HasOutcomes.Should().BeTrue();
            result.Markets.HasGoals.Should().BeTrue();
            result.RawResponseJson.Should().Contain("choices");
            result.InputHash.Should().HaveLength(64);
        }
        using var request = JsonDocument.Parse(handler.Request!);
        request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema")
            .GetProperty("required").EnumerateArray().Select(field => field.GetString())
            .Should().Contain("draw_probability").And.Contain("two_to_three_goals_probability");
    }

    [Fact]
    public async Task CombinedForecastNeverSendsAnotherProvidersKeyToOpenRouter()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var service = new OpenRouterForecastService(factory.Object,
            Options.Create(new OpenRouterOptions { ApiKey = "wrong-provider-key" }), NullLogger<OpenRouterForecastService>.Instance);
        (await service.ForecastCombinedAsync(new MatchAnalysis(), "nvidia/test:free")).Should().BeNull();
        factory.VerifyNoOtherCalls();
    }

    private sealed class Reply(double probability, bool full = false, double expectedGoals = 2.8) : HttpMessageHandler
    {
        public string? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = await request.Content!.ReadAsStringAsync(cancellationToken);
            var fields = new Dictionary<string, object>
            {
                ["expected_goals"] = expectedGoals, ["predicted_home_goals"] = 2, ["predicted_away_goals"] = 1,
                ["over_2_5_probability"] = probability, ["btts_probability"] = .6, ["confidence"] = .7,
                ["rationale"] = "Supplied scoring rates."
            };
            if (full)
            {
                fields["home_win_probability"] = .45;
                fields["draw_probability"] = .3;
                fields["away_win_probability"] = .25;
                fields["two_to_three_goals_probability"] = .5;
                fields["btts_and_over25_probability"] = .4;
            }
            var content = JsonSerializer.Serialize(fields);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content } } }
                }))
            };
        }
    }
}
