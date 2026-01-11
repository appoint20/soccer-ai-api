using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Repositories;

public class JsonFileLeaguesRepository : ILeaguesRepository
{
    private readonly ILogger<JsonFileLeaguesRepository> _logger;
    private readonly string _filePath;

    public JsonFileLeaguesRepository(IConfiguration configuration, ILogger<JsonFileLeaguesRepository> logger)
    {
        _logger = logger;
        var configPath = configuration["DataPaths:LeaguesJson"];
        _filePath = configPath ?? FindLeaguesJson();
    }

    private string FindLeaguesJson()
    {
        var locations = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "leagues.json");
        if (File.Exists(locations))
            _logger.LogError("leagues.json not found at {Path}", _filePath);
        
        return Path.GetFullPath(locations);
    }

    public async Task<List<LeagueDto>> GetLeaguesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(_filePath);
            var leagues = await JsonSerializer
                .DeserializeAsync<List<LeagueDto>>(stream, cancellationToken: cancellationToken);
            
            return leagues ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading leagues.json from {Path}", _filePath);
            throw;
        }
    }
}
