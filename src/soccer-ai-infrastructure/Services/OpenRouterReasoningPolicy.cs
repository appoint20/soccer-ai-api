using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json.Nodes;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Adds OpenRouter's <c>reasoning</c> field to chat-completion request bodies.
/// </summary>
/// <remarks>
/// The OpenAI SDK models OpenAI's schema, so there is nowhere to put a vendor
/// field: <c>ChatCompletionOptions</c> exposes <c>ReasoningEffortLevel</c>
/// (OpenAI's <c>reasoning_effort</c>) and nothing else. OpenRouter expects
/// <c>"reasoning": { "enabled": true }</c>, and a cloaked reasoning model such
/// as stealth/ox-alpha behaves differently depending on it. Rewriting the
/// serialized body in a per-call policy is the way to send a field the SDK does
/// not know about — the alternative is hand-rolling the whole request as
/// <see cref="BinaryContent"/> and losing the typed options with it.
/// </remarks>
public sealed class OpenRouterReasoningPolicy : PipelinePolicy
{
    private readonly JsonObject _reasoning;

    private OpenRouterReasoningPolicy(JsonObject reasoning) => _reasoning = reasoning;

    /// <summary>
    /// Builds the policy, or returns null when the request should be left alone
    /// so the provider's own default applies.
    /// </summary>
    public static OpenRouterReasoningPolicy? TryCreate(AiReasoningOptions? options)
    {
        if (options is null || !options.Send) return null;

        var reasoning = new JsonObject { ["enabled"] = options.Enabled };

        // Everything below only means something while reasoning is on; sending
        // an effort alongside "enabled": false is a contradiction the provider
        // would have to guess about.
        if (options.Enabled)
        {
            if (!string.IsNullOrWhiteSpace(options.Effort))
                reasoning["effort"] = options.Effort.Trim().ToLowerInvariant();
            if (options.MaxTokens is > 0)
                reasoning["max_tokens"] = options.MaxTokens;
            if (options.Exclude)
                reasoning["exclude"] = true;
        }

        return new OpenRouterReasoningPolicy(reasoning);
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (TryReadBody(message, out var body))
            WriteBody(message, body!);

        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (await TryReadBodyAsync(message).ConfigureAwait(false) is { } body)
            WriteBody(message, body);

        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static bool IsChatCompletion(PipelineMessage message) =>
        message.Request?.Content is not null &&
        message.Request.Uri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true;

    private static bool TryReadBody(PipelineMessage message, out JsonObject? body)
    {
        body = null;
        if (!IsChatCompletion(message)) return false;

        using var buffer = new MemoryStream();
        message.Request.Content!.WriteTo(buffer, message.CancellationToken);
        buffer.Position = 0;
        body = JsonNode.Parse(buffer) as JsonObject;
        return body is not null;
    }

    private static async ValueTask<JsonObject?> TryReadBodyAsync(PipelineMessage message)
    {
        if (!IsChatCompletion(message)) return null;

        using var buffer = new MemoryStream();
        await message.Request.Content!.WriteToAsync(buffer, message.CancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return JsonNode.Parse(buffer) as JsonObject;
    }

    private void WriteBody(PipelineMessage message, JsonObject body)
    {
        body["reasoning"] = _reasoning.DeepClone();

        var payload = Encoding.UTF8.GetBytes(body.ToJsonString());
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(payload));

        // The transport derives Content-Length from the content, but only when
        // the header is not already on the request. A stale length from the
        // pre-patch body would truncate the request.
        if (message.Request.Headers.TryGetValue("Content-Length", out _))
            message.Request.Headers.Set("Content-Length", payload.Length.ToString());
    }
}
