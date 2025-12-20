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

    // --- Persistent JSON Caching Implementation ---
    
    // Using file-based cache to persist across restarts (User Request)
    private readonly string _cacheDirectory;
    private readonly bool _isConfigValid; // Add specific flag

    public FootballApiService(HttpClient httpClient, ILogger<FootballApiService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["FootballApi:Key"] ?? "";
        _apiHost = configuration["FootballApi:Host"] ?? "api-football-v1.p.rapidapi.com";
        
        _isConfigValid = !string.IsNullOrEmpty(_apiKey) && _apiKey != "YOUR_API_KEY_HERE";

        if (!_isConfigValid)
        {
             _logger.LogWarning("FootballApi:Key is missing or placeholder. FootballApiService will return null/empty.");
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{_apiHost}/v3/");
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", _apiHost);
        }
        
        // Resolve cache directory: BaseDir/Data/cache/
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataPath = Path.Combine(baseDir, "Data", "cache");
        if (!Directory.Exists(dataPath))
        {
             try { Directory.CreateDirectory(dataPath); } catch { /* Ignore or log */ }
        }
        _cacheDirectory = dataPath;
    }
    
    public async Task<List<ApiFixture>?> GetTeamFixturesAsync(int teamId, int last = 5, int next = 2, CancellationToken cancellationToken = default)
    {
        if (!_isConfigValid) return null; // Safe exit
        // 1. Check Persistent File Cache (12 Hours)
        var cacheFile = Path.Combine(_cacheDirectory, $"schedule_{teamId}.json");
        
        if (File.Exists(cacheFile))
        {
            var info = new FileInfo(cacheFile);
            if (info.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-12))
            {
                try 
                {
                    using var stream = File.OpenRead(cacheFile);
                    var cached = await System.Text.Json.JsonSerializer.DeserializeAsync<List<ApiFixture>>(stream, cancellationToken: cancellationToken);
                    if (cached != null) return cached;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read cache file {File}, falling back to API", cacheFile);
                }
            }
        }

        try
        {
            // 2. Parallel API Calls (API-Football requires separate calls)
            var lastTask = FetchFixtures($"fixtures?team={teamId}&last={last}&status=FT-AET-PEN", cancellationToken);
            var nextTask = FetchFixtures($"fixtures?team={teamId}&next={next}", cancellationToken);

            await Task.WhenAll(lastTask, nextTask);

            var lastGames = await lastTask;
            var nextGames = await nextTask;

            var allFixtures = new List<ApiFixture>();
            if (lastGames != null) allFixtures.AddRange(lastGames);
            if (nextGames != null) allFixtures.AddRange(nextGames);

            // 3. Save to File (Persist) via Fire-and-Forget or Await
            if (allFixtures.Count > 0)
            {
                // We await to ensure data safety
                await SaveCacheAsync(cacheFile, allFixtures);
            }
            
            return allFixtures;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schedule for Team: {TeamId}", teamId);
            return null;
        }
    }
    
    private async Task SaveCacheAsync(string path, List<ApiFixture> data)
    {
        try
        {
            using var stream = File.Create(path);
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, data);
        }
        catch(Exception ex)
        {
             _logger.LogError(ex, "Failed to write cache file {Path}", path);
        }
    }
    
    private async Task<List<ApiFixture>?> FetchFixtures(string endpoint, CancellationToken ct)
    {
         var response = await _httpClient.GetFromJsonAsync<ApiFixtureResponse>(endpoint, ct);
         return response?.Response;
    }
    
    // Existing methods below
    public async Task<TeamStatsData?> GetTeamStatsAsync(int leagueId, int teamId, int season, CancellationToken cancellationToken)
    {
        if (!_isConfigValid) return null;
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
    
    // ... kept other methods ...

    public async Task<List<ApiFixture>?> GetFixturesAsync(int leagueId, int season, int next, CancellationToken cancellationToken)
    {
        if (!_isConfigValid) return null;
        try
        {
            var endpoint = $"fixtures?league={leagueId}&season={season}&next={next}";
            _logger.LogInformation("Fetching fixtures: {Endpoint}", endpoint);

            var response = await _httpClient.GetFromJsonAsync<ApiFixtureResponse>(endpoint, cancellationToken);
            return response?.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching fixtures for League: {LeagueId}", leagueId);
            return null;
        }
    }

    public async Task<ApiFootballPrediction?> GetPredictionAsync(int fixtureId, CancellationToken cancellationToken)
    {
        if (!_isConfigValid) return null;
        try
        {
            var endpoint = $"predictions?fixture={fixtureId}";
            _logger.LogInformation("Fetching prediction: {Endpoint}", endpoint);

            var response = await _httpClient.GetFromJsonAsync<ApiPredictionResponse>(endpoint, cancellationToken);
            return response?.Response.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching prediction for Fixture: {FixtureId}", fixtureId);
            return null;
        }
    }
}
