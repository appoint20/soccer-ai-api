using Microsoft.Extensions.Configuration;

namespace SoccerAi.Infrastructure.Services;

/// <summary>Resolve only credentials belonging to the configured provider.</summary>
public static class AiCredentials
{
    public static string Resolve(IConfiguration configuration, string? explicitKey = null,
        string? baseUrl = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var configured = new[] { explicitKey, configuration["AiService:ApiKey"] }
            .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
        if (configured is not null) return configured.Trim();
        var endpoint = baseUrl ?? configuration["AiService:BaseUrl"] ?? "https://openrouter.ai/api/v1";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return "";
        var variable = uri.Host.ToLowerInvariant() switch
        {
            "openrouter.ai" => "OPENROUTER_API_KEY",
            "api.z.ai" => "ZAI_API_KEY",
            "integrate.api.nvidia.com" => "NVIDIA_API_KEY",
            "api.anthropic.com" => "ANTHROPIC_API_KEY",
            _ => null
        };
        if (variable is null) return ""; // Custom compatible hosts require explicit configuration.
        return new[] { configuration[variable], environment(variable) }
            .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k))?.Trim() ?? "";
    }
}
