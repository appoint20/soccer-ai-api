using System.Net.Http.Json; // Fix GetFromJsonAsync
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Sync;

public class TeamStatsSyncService : ITeamStatsSyncService
{
    private readonly HttpClient _httpClient;
    private readonly ILeaguesRepository _leaguesRepository;
    private readonly ILogger<TeamStatsSyncService> _logger;
    private readonly string _outputDirectory;
    private readonly string _apiKey;
    private readonly string _apiHost;

    private const int CallsBeforeDelay = 25;
    private const int DelayMs = 20000; // 20 seconds

    private readonly bool _isConfigValid;

    public TeamStatsSyncService(HttpClient httpClient, ILeaguesRepository leaguesRepository, IConfiguration configuration, ILogger<TeamStatsSyncService> logger)
    {
        _httpClient = httpClient;
        _leaguesRepository = leaguesRepository;
        _logger = logger;
        
        _apiKey = configuration["FootballApi:Key"] ?? "";
        _apiHost = configuration["FootballApi:Host"] ?? "api-football-v1.p.rapidapi.com";
        
        _isConfigValid = !string.IsNullOrEmpty(_apiKey) && _apiKey != "YOUR_API_KEY_HERE";
        
        if (!_isConfigValid)
        {
            _logger.LogWarning("FootballApi:Key is missing or placeholder. TeamStatsSync will be disabled.");
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{_apiHost}/v3/");
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", _apiHost);
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _outputDirectory = Path.Combine(baseDir, "Data", "team_stats");
        if (!Directory.Exists(_outputDirectory)) Directory.CreateDirectory(_outputDirectory);
    }

    public async Task SyncTeamStatsAsync(CancellationToken cancellationToken)
    {
        if (!_isConfigValid)
        {
            _logger.LogError("Aborting Sync: API Key is invalid.");
            return;
        }

        _logger.LogInformation("Starting Team Stats Sync (Step 1)...");
        
        var leagues = await _leaguesRepository.GetLeaguesAsync(cancellationToken);
        int callCount = 0;
        int successCount = 0;

        foreach (var league in leagues)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger.LogInformation("Syncing League: {LeagueName} ({Id})", league.Name, league.ApiId);

            // 1. Get Teams for this League
            var teams = await FetchTeamsForLeague(league.ApiId, cancellationToken);
            if (teams == null) continue;

            callCount++; // Count the 'teams' call
            callCount = await CheckRateLimit(callCount, cancellationToken);

            foreach (var teamInfo in teams)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                int teamId = teamInfo.Team.Id;
                string teamName = teamInfo.Team.Name;
                
                // 2. Fetch Stats for this Team
                try
                {
                    await FetchAndSaveStats(league.ApiId, teamId, teamName, cancellationToken);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync stats for {TeamName} ({Id})", teamName, teamId);
                }

                callCount++;
                callCount = await CheckRateLimit(callCount, cancellationToken);
            }
        }
        
        _logger.LogInformation("Team Stats Sync Complete. Synced {Count} teams.", successCount);
    }

    private async Task<int> CheckRateLimit(int count, CancellationToken ct)
    {
        if (count >= CallsBeforeDelay)
        {
            _logger.LogInformation("Rate limit safeguard: Pausing for {Seconds}s...", DelayMs / 1000);
            await Task.Delay(DelayMs, ct);
            return 0; // Reset
        }
        return count;
    }

    private async Task<List<ApiTeamResponse>?> FetchTeamsForLeague(int leagueId, CancellationToken ct)
    {
        try
        {
            // Season 2025 hardcoded for now or fetch dynamic
            var response = await _httpClient.GetFromJsonAsync<ApiTeamListResponse>($"teams?league={leagueId}&season=2025", ct);
            return response?.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching teams for league {LeagueId}", leagueId);
            return null;
        }
    }

    private async Task FetchAndSaveStats(int leagueId, int teamId, string teamName, CancellationToken ct)
    {
        var filePath = Path.Combine(_outputDirectory, $"{teamId}.json");
        
        var endpoint = $"teams/statistics?league={leagueId}&team={teamId}&season=2025";
        var responseString = await _httpClient.GetStringAsync(endpoint, ct);

        // Validate valid JSON before writing
        using var doc = JsonDocument.Parse(responseString); 
        
        await File.WriteAllTextAsync(filePath, responseString, ct);
        _logger.LogDebug("Saved stats for {Team} to {Path}", teamName, filePath);
    }
}

// Private DTOs for this service
class ApiTeamListResponse { public List<ApiTeamResponse> Response { get; set; } = new(); }
class ApiTeamResponse { public ApiTeamInfo Team { get; set; } = new(); }
class ApiTeamInfo { public int Id { get; set; } public string Name { get; set; } = ""; }
