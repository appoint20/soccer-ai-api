
using System.Text.Json;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Repositories;

public class JsonFileLocalTeamStatsRepository : ILocalTeamStatsRepository
{
    private readonly ILogger<JsonFileLocalTeamStatsRepository> _logger;
    private readonly string _baseDataPath;

    public JsonFileLocalTeamStatsRepository(ILogger<JsonFileLocalTeamStatsRepository> logger)
    {
        _logger = logger;
        _baseDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "team_stats");
    }

    public async Task<TeamStatsData?> GetTeamStatsByNameAsync(string leagueName, string teamName, CancellationToken cancellationToken)
    {
        // 1. Find Data/team_stats/{leagueName}/2025/teams
        var leaguePath = Path.Combine(_baseDataPath, leagueName);
        
        // Handle common variations in folder names vs league names from CSV
        if (!Directory.Exists(leaguePath))
        {
            // Simple mapping or fuzzy attempt?
            // CSV might say "B1" but folder is "Jupiler_Pro_League" (example). 
            // In fixtures.csv we saw "B1", "E0" etc. or we need to map "Div". 
            // Let's assume for now we might need a mapper.
            // But if user asks to read from fixtures.csv, and query stats, we need to map.
            
            // For MVP, lets try to find any folder containing partial match or proceed.
            // But actually, team stats are organized by Folder Name (e.g. Premier_League).
            // fixtures.csv has "Div" (e.g. B1, E0).
            return null;
        }

        var teamsPath = Path.Combine(leaguePath, "2025", "teams");
        if (!Directory.Exists(teamsPath)) return null;

        // 2. Iterate all JSONs to find matching team name
        // This is inefficient but functional for small datasets. 
        // A better approach would be to cache this map on startup.
        var files = Directory.GetFiles(teamsPath, "*_stats.json");
        foreach (var file in files)
        {
            try 
            {
                using var stream = File.OpenRead(file);
                var data = await JsonSerializer.DeserializeAsync<TeamStatsData>(stream, cancellationToken: cancellationToken);
                
                if (data?.Team?.Name != null && IsNameMatch(teamName, data.Team.Name))
                {
                    return data;
                }
            }
            catch 
            { 
                continue; 
            }
        }

        return null;
    }

    private bool IsNameMatch(string searchName, string jsonName)
    {
        if (string.Equals(searchName, jsonName, StringComparison.OrdinalIgnoreCase)) return true;
        
        // Simple fuzzy contained
        return jsonName.Contains(searchName, StringComparison.OrdinalIgnoreCase) 
            || searchName.Contains(jsonName, StringComparison.OrdinalIgnoreCase);
    }
}
