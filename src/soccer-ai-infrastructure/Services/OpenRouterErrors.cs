using System.ClientModel;
using System.Text.Json;

namespace SoccerAi.Infrastructure.Services;

public static class OpenRouterErrors
{
    public static bool IsProviderRateLimit(ClientResultException exception)
    {
        if (exception.Status != 429) return false;
        try
        {
            var response = exception.GetRawResponse();
            if (response?.Headers.TryGetValue("X-RateLimit-Limit", out _) == true)
                return false;
            return IsProviderRateLimit(response?.Content?.ToString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsProviderRateLimit(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object) return false;
            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String &&
                message.GetString()!.Contains("free-models-per-", StringComparison.OrdinalIgnoreCase)) return false;
            if (!error.TryGetProperty("metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object) return false;
            return metadata.TryGetProperty("provider_code", out var code) && code.ValueKind is not (JsonValueKind.Null or JsonValueKind.False)
                || metadata.TryGetProperty("provider_name", out var provider) &&
                provider.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(provider.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
