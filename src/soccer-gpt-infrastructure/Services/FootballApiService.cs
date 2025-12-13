using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class FootballApiService : IFootballApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FootballApiService> _logger;
    private readonly string _apiKey;
    private readonly string _apiHost;

    public FootballApiService(HttpClient httpClient, ILogger<FootballApiService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["FootballApi:Key"] ?? throw new ArgumentNullException("FootballApi:Key is not configured");
        _apiHost = configuration["FootballApi:Host"] ?? "api-football-v1.p.rapidapi.com";

        _httpClient.BaseAddress = new Uri($"https://{_apiHost}/v3/");
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", _apiHost);
    }

    public async Task<TeamStatsData?> GetTeamStatsAsync(int leagueId, int teamId, int season, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = $"teams/statistics?league={leagueId}&team={teamId}&season={season}";
            _logger.LogInformation("Fetching stats: {Endpoint}", endpoint);

            var response = await _httpClient.GetFromJsonAsync<TeamStatsResponse>(endpoint, cancellationToken);
            
            return response?.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching team stats for League: {LeagueId}, Team: {TeamId}", leagueId, teamId);
            return null;
        }
    }
}
