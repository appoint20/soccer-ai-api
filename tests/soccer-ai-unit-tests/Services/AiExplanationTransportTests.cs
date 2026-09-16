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

public class AiExplanationTransportTests
{
    [Fact]
    public async Task JsonObjectWithNestedSummaryAndMarketArraysSurvivesTheActualProviderAdapter()
    {
        // No external AI calls or credentials: exercise the real SDK/adapter
        // against a loopback completion endpoint, including its JSON extraction.
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var match = new MatchAnalysis { Id = 123, Date = DateTimeOffset.UtcNow.AddHours(4) };
        var input = DecisionExplanationPolicy.Input(match);
        var result = new AiDecisionExplanation
        {
            FixtureId = 123, InputHash = "forged", ModelVersion = "forged", GeneratedAtUtc = DateTimeOffset.MinValue,
            En = new() { SummaryLines = ["The sides are balanced.", "Scoring evidence is mixed.", "Neither has a clear edge.", "Limited data adds uncertainty."] },
            De = new() { SummaryLines = ["Die Teams sind ausgeglichen.", "Die Tor-Daten sind gemischt.", "Kein Team hat klare Vorteile.", "Wenige Daten erhöhen die Unsicherheit."] }
        };
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(timeout.Token);
            body.Should().Contain("COMPLETED football prediction decision");
            var response = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-test", @object = "chat.completion", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model = "test-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = JsonSerializer.Serialize(result) }, finish_reason = "stop" } }
            });
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
        }, timeout.Token);
        var options = new AiServiceOptions { ApiKey = "local-test-key", BaseUrl = $"http://127.0.0.1:{port}",
            DefaultModel = "test-model", FallbackModel = "", MaxRetries = 0, Reasoning = new() { Send = false } };
        var provider = new OpenAiAnalysisService(Options.Create(options), new ConfigurationBuilder().Build(),
            NullLogger<OpenAiAnalysisService>.Instance);
        var actual = await provider.ExplainDecisionAsync(input, timeout.Token);
        await server;
        actual!.En.SummaryLines.Should().HaveCount(4);
        actual.InputHash.Should().Be(DecisionExplanationPolicy.Hash(input));
        actual.ModelVersion.Should().Be("test-model");
        actual.GeneratedAtUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
