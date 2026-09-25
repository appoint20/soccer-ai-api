using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

public class AiNarrativeLengthTests
{
    private static AiBilingualResult Result() => new()
    {
        FixtureId = 123,
        En = new() { Analysis = string.Join(" ", AiSummarySamples.English) },
        De = new() { Analysis = string.Join(" ", AiSummarySamples.German) }
    };

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void CompleteSummariesInBothLanguagesAreAccepted(int count)
    {
        var result = Result();
        for (var i = 4; i < count; i++)
        {
            result.En.Analysis += " A quieter scoring pattern remains possible when the attacks do not convert their chances.";
            result.De.Analysis += " Ein torärmeres Spiel bleibt möglich, wenn die Angriffe ihre Chancen nicht nutzen.";
        }
        AiNarrativeIntegrity.InvalidAnalysisLength(result).Should().BeNull();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    [InlineData(false, 9)]
    public void TooFewOrTooManySentencesInEitherLanguageAreRejected(bool german, int count)
    {
        var result = Result();
        (german ? result.De : result.En).Analysis = string.Join(" ",
            Enumerable.Repeat(AiSummarySamples.English[0], count));
        AiNarrativeIntegrity.InvalidAnalysisLength(result).Should().NotBeNull();
    }

    [Fact]
    public void SentenceCountDoesNotSplitDecimalGoalLines()
    {
        const string text = "The estimate for Over 2.5 is uncertain. Die Chance für über 2,5 Tore ist unsicher.";
        AiNarrativeIntegrity.SentenceCount(text).Should().Be(2);
    }

    [Fact]
    public void FourClippedLabelsCannotPassAsAnAnalysis()
    {
        var result = Result();
        result.De.Analysis = "Offenes Spiel. Schwache Abwehr. Unsicherer Angriff. Wenige Daten.";
        AiNarrativeIntegrity.InvalidAnalysisLength(result).Should().NotBeNull();
    }

    /// <summary>
    /// A short response is never persisted. The model is first shown its own
    /// answer and the complaint; only when it repeats the mistake does the run
    /// move to the fallback model.
    /// </summary>
    [Fact]
    public async Task AShortResponseIsRetriedOnceThenGivenToTheFallback()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requestedModels = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var reader = new StreamReader(context.Request.InputStream);
                using var request = JsonDocument.Parse(await reader.ReadToEndAsync(timeout.Token));
                requestedModels.Add(request.RootElement.GetProperty("model").GetString()!);
                var result = Result();
                if (attempt < 2) result.De.Analysis = "Die Teams sind ausgeglichen.";
                var response = JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-test", @object = "chat.completion", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    model = requestedModels[^1],
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = JsonSerializer.Serialize(new[] { result }) }, finish_reason = "stop" } }
                });
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
            }
        }, timeout.Token);
        var options = new AiServiceOptions
        {
            ApiKey = "local-test-key", BaseUrl = $"http://127.0.0.1:{port}", DefaultModel = "test-primary",
            FallbackModel = "test-fallback", MaxRetries = 0, Reasoning = new() { Send = false }
        };
        var provider = new OpenAiAnalysisService(Options.Create(options), new ConfigurationBuilder().Build(),
            NullLogger<OpenAiAnalysisService>.Instance);
        var actual = await provider.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }], timeout.Token);
        await server;
        // The primary gets one corrective attempt before another model is paid for.
        requestedModels.Should().Equal("test-primary", "test-primary", "test-fallback");
        actual[123].ModelVersion.Should().Be("test-fallback");
        actual[123].De.Analysis.Should().Be(Result().De.Analysis);
    }

    /// <summary>
    /// The case the repair exists for: the model writes valid JSON and misses
    /// one length rule, then fixes it when told. Production had narratives on 2
    /// of 50 upcoming matches because that first answer was simply discarded.
    /// </summary>
    [Fact]
    public async Task ACorrectedResponseIsKeptWithoutPayingForAnotherModel()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requestedModels = new List<string>();
        var complaints = new List<string>();
        var server = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync(timeout.Token);
                using var request = JsonDocument.Parse(body);
                requestedModels.Add(request.RootElement.GetProperty("model").GetString()!);
                if (body.Contains("was rejected")) complaints.Add(body);
                var result = Result();
                if (attempt == 0) result.De.Analysis = "Die Teams sind ausgeglichen.";
                var response = JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-test", @object = "chat.completion", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    model = requestedModels[^1],
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = JsonSerializer.Serialize(new[] { result }) }, finish_reason = "stop" } }
                });
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
            }
        }, timeout.Token);
        var options = new AiServiceOptions
        {
            ApiKey = "local-test-key", BaseUrl = $"http://127.0.0.1:{port}", DefaultModel = "test-primary",
            FallbackModel = "test-fallback", MaxRetries = 0, Reasoning = new() { Send = false }
        };
        var provider = new OpenAiAnalysisService(Options.Create(options), new ConfigurationBuilder().Build(),
            NullLogger<OpenAiAnalysisService>.Instance);

        var actual = await provider.AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }], timeout.Token);
        await server;

        requestedModels.Should().Equal("test-primary", "test-primary");
        requestedModels.Should().NotContain("test-fallback", "the correction removed any need for another model");
        actual[123].ModelVersion.Should().Be("test-primary");
        actual[123].De.Analysis.Should().Be(Result().De.Analysis);
        complaints.Should().ContainSingle("the retry carries the reason the first answer was rejected");
    }
}
