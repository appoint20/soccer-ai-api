using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class AiModelAvailabilityTests
{
    [Fact]
    public async Task EmptyModelConfigurationCannotSilentlySelectAPaidModel()
    {
        var service = Service(new AiServiceOptions
        {
            ApiKey = "sk-or-test", DefaultModel = "", FallbackModel = ""
        });
        var run = () => service.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);
        (await run.Should().ThrowAsync<ExternalApiException>()).Which.StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ProviderOverloadUsesFallbackButPlatformQuotaStopsTheRun(bool upstream, bool platformHeader)
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var expectedFallback = upstream && !platformHeader;
        var requestedModels = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < (expectedFallback ? 2 : 1); attempt++)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var reader = new StreamReader(context.Request.InputStream);
                using var request = JsonDocument.Parse(await reader.ReadToEndAsync(timeout.Token));
                requestedModels.Add(request.RootElement.GetProperty("model").GetString()!);
                request.RootElement.GetProperty("reasoning").GetProperty("enabled").GetBoolean().Should().BeFalse();
                string body;
                if (attempt == 0)
                {
                    context.Response.StatusCode = 429;
                    if (platformHeader) context.Response.Headers.Add("X-RateLimit-Limit", "50");
                    body = upstream
                        ? """{"error":{"code":429,"message":"Provider busy","metadata":{"provider_name":"Nvidia"}}}"""
                        : """{"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day"}}""";
                }
                else
                {
                    var result = new AiBilingualResult
                    {
                        FixtureId = 123,
                        En = new() { Analysis = string.Join(" ", AiSummarySamples.English) },
                        De = new() { Analysis = string.Join(" ", AiSummarySamples.German) }
                    };
                    body = JsonSerializer.Serialize(new
                    {
                        id = "chatcmpl-test", @object = "chat.completion", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        model = requestedModels[^1],
                        choices = new[] { new { index = 0, message = new { role = "assistant", content = JsonSerializer.Serialize(new[] { result }) }, finish_reason = "stop" } }
                    });
                }
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
                context.Response.Close();
            }
        }, timeout.Token);
        var service = Service(new AiServiceOptions
        {
            ApiKey = "local-test-key", BaseUrl = $"http://127.0.0.1:{port}",
            DefaultModel = "test-primary:free", FallbackModel = "test-fallback:free", MaxRetries = 0
        });
        var run = () => service.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }], timeout.Token);
        if (expectedFallback)
            (await run())[123].ModelVersion.Should().Be("test-fallback:free");
        else
            (await run.Should().ThrowAsync<ExternalApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        await server;
        requestedModels.Should().HaveCount(expectedFallback ? 2 : 1);
    }

    private static OpenAiAnalysisService Service(AiServiceOptions options) => new(
        Options.Create(options), new ConfigurationBuilder().Build(), NullLogger<OpenAiAnalysisService>.Instance);
}
