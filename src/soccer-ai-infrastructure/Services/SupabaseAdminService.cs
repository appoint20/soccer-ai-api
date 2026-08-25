using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;

namespace SoccerAi.Infrastructure.Services;

/// <inheritdoc />
public sealed class SupabaseAdminService(
    HttpClient http,
    IOptions<SupabaseOptions> options,
    ILogger<SupabaseAdminService> logger) : ISupabaseAdminService
{
    public const string HttpClientName = "supabase-admin";

    private readonly SupabaseOptions _options = options.Value;

    public bool IsConfigured =>
        _options.IsConfigured && !string.IsNullOrWhiteSpace(_options.ServiceRoleKey);

    public async Task<bool> VerifyPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = JsonContent.Create(new { email, password })
        };
        // The anon-level call is enough here, but the service key is a valid
        // apikey too and avoids configuring a second secret.
        request.Headers.Add("apikey", _options.ServiceRoleKey);

        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_options.AdminUsersUrl}/{userId}");
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "[Supabase] Deleting user {UserId} failed with {Status}",
                userId, (int)response.StatusCode);
            return false;
        }

        logger.LogInformation("[Supabase] User {UserId} deleted their account", userId);
        return true;
    }
}
