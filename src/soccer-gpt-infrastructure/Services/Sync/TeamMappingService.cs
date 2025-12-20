using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services.Sync;

public class TeamMappingService : ITeamMappingService
{
    private readonly ILogger<TeamMappingService> _logger;
    private readonly string _inputDirectory;
    private readonly string _outputFile;

    public TeamMappingService(ILogger<TeamMappingService> logger)
    {
        _logger = logger;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _inputDirectory = Path.Combine(baseDir, "Data", "team_stats");
        _outputFile = Path.Combine(baseDir, "Data", "team_mapping.json");
    }

    public async Task MapTeamsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Team Mapping (Step 2)...");

        if (!Directory.Exists(_inputDirectory))
        {
            _logger.LogWarning("No stats directory found at {Path}. Skipping mapping.", _inputDirectory);
            return;
        }

        var files = Directory.GetFiles(_inputDirectory, "*.json");
        var mappingList = new List<TeamMappingDto>();

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                using var stream = File.OpenRead(file);
                var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;
                
                // Navigate: response -> team -> id/name AND response -> league -> id/name
                if (root.TryGetProperty("response", out var response))
                {
                    // Access 'team'
                    if (response.TryGetProperty("team", out var team))
                    {
                        var id = team.GetProperty("id").GetInt32();
                        var name = team.GetProperty("name").GetString() ?? "Unknown";
                        
                        // Access 'league'
                        int leagueId = 0;
                        string leagueName = "";
                        if (response.TryGetProperty("league", out var league))
                        {
                            leagueId = league.GetProperty("id").GetInt32();
                            leagueName = league.GetProperty("name").GetString() ?? "";
                        }

                        // Determine CSV Name (For now, use API Name; can add manual overrides/fuzzy logic here if needed)
                        // Requirement says: "create new object in which team name and id and fixture.csv name is contained"
                        // Assuming CSV Name ~= API Name for newly generated files, or we map it.
                        // For fully automated, we treat API Name as the source of truth for new CSVs.
                        
                        mappingList.Add(new TeamMappingDto
                        {
                            Id = id,
                            Name = name,
                            CsvName = name, // Default to API name
                            LeagueId = leagueId,
                            LeagueName = leagueName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse {File}: {Msg}", Path.GetFileName(file), ex.Message);
            }
        }

        // Save
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(_outputFile, JsonSerializer.Serialize(mappingList, options), cancellationToken);
        
        _logger.LogInformation("Mapped {Count} teams to {File}", mappingList.Count, _outputFile);
    }
}

public class TeamMappingDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string CsvName { get; set; } = "";
    public int LeagueId { get; set; }
    public string LeagueName { get; set; } = "";
}
