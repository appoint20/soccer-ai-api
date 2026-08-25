using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Services;
using Xunit;

namespace SoccerAi.Tests.Services;

/// <summary>
/// The policy rewrites a body the OpenAI SDK serialized, so the assertions are
/// on the bytes that actually reach the transport — patching JSON by hand is
/// exactly the kind of thing that silently sends a truncated or unchanged
/// request.
/// </summary>
public class OpenRouterReasoningPolicyTests
{
    private const string ChatUrl = "https://openrouter.ai/api/v1/chat/completions";

    private static async Task<string?> SendAsync(AiReasoningOptions options, string url, string body)
    {
        var handler = new CapturingHandler();
        var clientOptions = new ClientPipelineOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        };

        var policy = OpenRouterReasoningPolicy.TryCreate(options);
        if (policy is not null)
            clientOptions.AddPolicy(policy, PipelinePosition.PerCall);

        var pipeline = ClientPipeline.Create(clientOptions);
        var message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri(url);
        message.Request.Headers.Set("Content-Type", "application/json");
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(body));

        await pipeline.SendAsync(message);
        return handler.Body;
    }

    [Fact]
    public async Task Adds_reasoning_object_to_chat_completion_requests()
    {
        var sent = await SendAsync(
            new AiReasoningOptions { Enabled = true, Effort = "High", MaxTokens = 2048 },
            ChatUrl,
            """{"model":"stealth/ox-alpha","messages":[]}""");

        var reasoning = JsonDocument.Parse(sent!).RootElement.GetProperty("reasoning");
        reasoning.GetProperty("enabled").GetBoolean().Should().BeTrue();
        reasoning.GetProperty("effort").GetString().Should().Be("high");
        reasoning.GetProperty("max_tokens").GetInt32().Should().Be(2048);
        reasoning.GetProperty("exclude").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Keeps_the_rest_of_the_body_intact()
    {
        var sent = await SendAsync(
            new AiReasoningOptions(),
            ChatUrl,
            """{"model":"stealth/ox-alpha","max_completion_tokens":8192}""");

        var root = JsonDocument.Parse(sent!).RootElement;
        root.GetProperty("model").GetString().Should().Be("stealth/ox-alpha");
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(8192);
    }

    [Fact]
    public async Task Disabling_reasoning_sends_only_the_off_switch()
    {
        var sent = await SendAsync(
            new AiReasoningOptions { Enabled = false, Effort = "high" },
            ChatUrl,
            """{"model":"stealth/ox-alpha"}""");

        var reasoning = JsonDocument.Parse(sent!).RootElement.GetProperty("reasoning");
        reasoning.GetProperty("enabled").GetBoolean().Should().BeFalse();
        reasoning.TryGetProperty("effort", out _).Should().BeFalse();
    }

    [Fact]
    public void Send_false_leaves_the_provider_default_in_place()
    {
        OpenRouterReasoningPolicy.TryCreate(new AiReasoningOptions { Send = false }).Should().BeNull();
    }

    [Fact]
    public async Task Leaves_other_endpoints_untouched()
    {
        var sent = await SendAsync(
            new AiReasoningOptions(),
            "https://openrouter.ai/api/v1/models",
            """{"model":"stealth/ox-alpha"}""");

        JsonDocument.Parse(sent!).RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
                RequestMessage = request
            };
        }
    }
}
