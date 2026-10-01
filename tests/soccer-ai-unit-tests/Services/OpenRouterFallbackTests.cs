using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Free models fail in ways a paid one rarely does: the provider behind the
/// slug is busy, or OpenRouter answers 200 with an error and no reply at all.
/// Those cost one model one fixture. Only OpenRouter's own cap stops the run.
/// </summary>
public class OpenRouterFallbackTests
{
    private const string Primary = "test-primary:free";
    private const string Fallback = "test-fallback:free";

    private const string UpstreamRefusal = """
        {"error":{"code":429,"message":"Provider returned error",
         "metadata":{"provider_name":"Nvidia","raw":"test-primary:free is temporarily rate-limited upstream"}}}
        """;

    [Fact]
    public void TheUpstreamReasonIsLiftedFromMetadata()
    {
        OpenAiAnalysisService.DescribeProviderError(UpstreamRefusal).Should().Be(
            "Provider returned error (Nvidia: test-primary:free is temporarily rate-limited upstream)");
    }

    /// <summary>
    /// Production saw this from a free NVIDIA endpoint in half a second, and
    /// logged only "Specified argument was out of the range of valid values".
    /// </summary>
    [Fact]
    public async Task AnEmptyReplyIsNamedAndTheFallbackAnswers()
    {
        var logger = new ListLogger();
        await using var server = new FakeOpenRouter(attempt => attempt == 0 ? (200, UpstreamRefusal) : (200, null));

        var actual = await Service(server.Port, logger).AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);

        // No corrective round trip: there was no answer to correct.
        server.RequestedModels.Should().Equal(Primary, Fallback);
        actual[123].ModelVersion.Should().Be(Fallback);
        logger.Lines.Should().Contain(line => line.Contains(
            "The provider returned no answer: Provider returned error (Nvidia: test-primary:free is temporarily rate-limited upstream)"));
    }

    [Fact]
    public async Task ABusyProviderCostsOnlyThatModel()
    {
        await using var server = new FakeOpenRouter(attempt => attempt == 0 ? (429, UpstreamRefusal) : (200, null));

        var actual = await Service(server.Port).AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);

        server.RequestedModels.Should().Equal(Primary, Fallback);
        actual[123].ModelVersion.Should().Be(Fallback);
    }

    /// <summary>OpenRouter's daily cap applies to every free model, so trying another is pointless.</summary>
    [Fact]
    public async Task ThePlatformCapStillStopsTheRun()
    {
        await using var server = new FakeOpenRouter(_ =>
            (429, """{"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day"}}"""));

        var run = () => Service(server.Port).AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);

        (await run.Should().ThrowAsync<ExternalApiException>()).Which.StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);
        server.RequestedModels.Should().Equal(Primary);
    }

    /// <summary>
    /// A skipped primary can never succeed, so a count shared by the whole
    /// process never reset: one busy spell retired the primary until restart.
    /// </summary>
    [Fact]
    public async Task ASkippedPrimaryIsAskedAgainOnTheNextRun()
    {
        await using var server = new FakeOpenRouter(attempt => attempt == 0 ? (200, UpstreamRefusal) : (200, null));

        await Service(server.Port, skipAfter: 1).AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);
        var nextRun = await Service(server.Port, skipAfter: 1).AnalyzeBatchAsync([new AiBatchItem { FixtureId = 123 }]);

        server.RequestedModels.Should().Equal(Primary, Fallback, Primary);
        nextRun[123].ModelVersion.Should().Be(Primary);
    }

    private static OpenAiAnalysisService Service(int port, ILogger<OpenAiAnalysisService>? logger = null, int skipAfter = 3) => new(
        Options.Create(new AiServiceOptions
        {
            ApiKey = "local-test-key", BaseUrl = $"http://127.0.0.1:{port}", DefaultModel = Primary,
            FallbackModel = Fallback, MaxRetries = 0, PrimaryFailuresBeforeSkip = skipAfter,
            Reasoning = new() { Send = false }
        }),
        new ConfigurationBuilder().Build(),
        logger ?? new ListLogger());

    private static string Completion(string model) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test", @object = "chat.completion", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = JsonSerializer.Serialize(new[]
                    {
                        new AiBilingualResult
                        {
                            FixtureId = 123,
                            En = new() { Analysis = string.Join(" ", AiSummarySamples.English) },
                            De = new() { Analysis = string.Join(" ", AiSummarySamples.German) }
                        }
                    })
                },
                finish_reason = "stop"
            }
        }
    });

    /// <summary>
    /// Answers each request with the status and body chosen for its attempt
    /// number; a null body is a valid analysis from the model that was asked.
    /// </summary>
    private sealed class FakeOpenRouter : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private readonly Task _serving;

        public FakeOpenRouter(Func<int, (int Status, string? Body)> respond)
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start(); Port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _serving = Task.Run(async () =>
            {
                for (var attempt = 0; ; attempt++)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                    using var reader = new StreamReader(context.Request.InputStream);
                    using var request = JsonDocument.Parse(await reader.ReadToEndAsync(_stop.Token));
                    var model = request.RootElement.GetProperty("model").GetString()!;
                    lock (RequestedModels) RequestedModels.Add(model);

                    var (status, body) = respond(attempt);
                    var bytes = Encoding.UTF8.GetBytes(body ?? Completion(model));
                    context.Response.StatusCode = status;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, _stop.Token);
                    context.Response.Close();
                }
            });
        }

        public int Port { get; }

        public List<string> RequestedModels { get; } = [];

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try { await _serving; } catch (OperationCanceledException) { }
            _listener.Close();
            _stop.Dispose();
        }
    }

    private sealed class ListLogger : ILogger<OpenAiAnalysisService>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add(formatter(state, exception));
        }
    }
}
