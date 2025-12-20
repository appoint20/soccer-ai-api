using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class EuropeanFixturesService : IEuropeanFixturesService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EuropeanFixturesService> _logger;
    private readonly EuropeanFixturesOptions _options;
    private readonly string _dataDirectory;
    
    private static readonly List<EuropeanCompetition> Competitions = new()
    {
        new() { Id = 42, Name = "Champions League", ShortCode = "UCL" },
        new() { Id = 73, Name = "Europa League", ShortCode = "UEL" }
    };

    public EuropeanFixturesService(
        IHttpClientFactory httpClientFactory,
        ILogger<EuropeanFixturesService> logger,
        IOptions<EuropeanFixturesOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient("EuropeanFixturesApi");
        _logger = logger;
        _options = options.Value;
        _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data", "european_fixtures");
        
        Directory.CreateDirectory(_dataDirectory);
    }

    public async Task<bool> UpdateEuropeanFixturesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting European fixtures update");
            
            var allTeamFixtures = new Dictionary<string, TeamEuropeanFixtures>();
            
            foreach (var competition in Competitions)
            {
                _logger.LogInformation("Fetching {CompetitionName} fixtures...", competition.Name);
                
                var fixtures = await FetchFixturesForCompetitionAsync(competition, cancellationToken);
                
                if (fixtures == null)
                {
                    _logger.LogWarning("Failed to fetch {CompetitionName} fixtures", competition.Name);
                    continue;
                }
                
                // Save raw competition data
                await SaveRawFixturesAsync(competition.ShortCode, fixtures, cancellationToken);
                
                // Process for team-based lookup
                ProcessTeamFixtures(fixtures, competition.ShortCode, allTeamFixtures);
                
                _logger.LogInformation("Processed {Count} {Competition} matches", 
                    fixtures.Response?.Matches.Count ?? 0, competition.Name);
            }
            
            // Calculate recent/upcoming and save team lookup
            ProcessRecentAndUpcoming(allTeamFixtures);
            await SaveTeamLookupAsync(allTeamFixtures, cancellationToken);
            
            _logger.LogInformation("European fixtures update completed. Total teams: {TeamCount}", 
                allTeamFixtures.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating European fixtures");
            return false;
        }
    }

    public async Task<TeamEuropeanFixtures?> GetTeamFixturesAsync(string teamName, CancellationToken cancellationToken = default)
    {
        try
        {
            var lookup = await LoadTeamLookupAsync(cancellationToken);
            return lookup?.GetValueOrDefault(teamName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team fixtures for {TeamName}", teamName);
            return null;
        }
    }

    public async Task<bool> HasRecentEuropeanMatchesAsync(string teamName, CancellationToken cancellationToken = default)
    {
        var fixtures = await GetTeamFixturesAsync(teamName, cancellationToken);
        return fixtures?.HasRecentEuropean ?? false;
    }

    public async Task<bool> HasUpcomingEuropeanMatchesAsync(string teamName, CancellationToken cancellationToken = default)
    {
        var fixtures = await GetTeamFixturesAsync(teamName, cancellationToken);
        return fixtures?.HasUpcomingEuropean ?? false;
    }

    public async Task<List<string>> GetAllEuropeanTeamsAsync(CancellationToken cancellationToken = default)
    {
        var lookup = await LoadTeamLookupAsync(cancellationToken);
        return lookup?.Keys.ToList() ?? new List<string>();
    }

    private async Task<EuropeanFixturesApiResponse?> FetchFixturesForCompetitionAsync(
        EuropeanCompetition competition, 
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/football-get-all-matches-by-league?leagueid={competition.Id}", 
                cancellationToken);
            
            response.EnsureSuccessStatusCode();
            
            var data = await response.Content.ReadFromJsonAsync<EuropeanFixturesApiResponse>(cancellationToken);
            
            if (data?.Status == "success")
            {
                return data;
            }
            
            _logger.LogWarning("API returned non-success status for {Competition}", competition.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching fixtures for {Competition}", competition.Name);
            return null;
        }
    }

    private void ProcessTeamFixtures(
        EuropeanFixturesApiResponse fixturesData, 
        string competitionCode, 
        Dictionary<string, TeamEuropeanFixtures> allTeamFixtures)
    {
        if (fixturesData?.Response?.Matches == null) return;
        
        foreach (var match in fixturesData.Response.Matches)
        {
            var homeTeam = match.Home.Name;
            var awayTeam = match.Away.Name;
            
            if (string.IsNullOrEmpty(homeTeam) || string.IsNullOrEmpty(awayTeam))
                continue;
            
            DateTime? parsedDate = null;
            if (!string.IsNullOrEmpty(match.Status.UtcTime))
            {
                if (DateTime.TryParse(match.Status.UtcTime, out var dt))
                    parsedDate = dt;
            }
            
            var fixture = new ProcessedFixture
            {
                MatchId = match.Id,
                Date = match.Status.UtcTime,
                DateParsed = parsedDate,
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                HomeScore = match.Home.Score,
                AwayScore = match.Away.Score,
                Competition = competitionCode,
                Finished = match.Status.Finished,
                Started = match.Status.Started
            };
            
            // Add to home team
            EnsureTeamExists(homeTeam, allTeamFixtures);
            var homeFixture = fixture with { Venue = "home" };
            if (competitionCode == "UCL")
                allTeamFixtures[homeTeam].UclFixtures.Add(homeFixture);
            else
                allTeamFixtures[homeTeam].UelFixtures.Add(homeFixture);
            
            // Add to away team
            EnsureTeamExists(awayTeam, allTeamFixtures);
            var awayFixture = fixture with { Venue = "away" };
            if (competitionCode == "UCL")
                allTeamFixtures[awayTeam].UclFixtures.Add(awayFixture);
            else
                allTeamFixtures[awayTeam].UelFixtures.Add(awayFixture);
        }
    }

    private static void EnsureTeamExists(string teamName, Dictionary<string, TeamEuropeanFixtures> allTeamFixtures)
    {
        if (!allTeamFixtures.ContainsKey(teamName))
        {
            allTeamFixtures[teamName] = new TeamEuropeanFixtures
            {
                TeamName = teamName,
                UclFixtures = new List<ProcessedFixture>(),
                UelFixtures = new List<ProcessedFixture>(),
                RecentMatches = new List<ProcessedFixture>(),
                UpcomingMatches = new List<ProcessedFixture>()
            };
        }
    }

    private void ProcessRecentAndUpcoming(Dictionary<string, TeamEuropeanFixtures> allTeamFixtures)
    {
        var now = DateTime.UtcNow;
        
        foreach (var (teamName, teamData) in allTeamFixtures)
        {
            var allFixtures = teamData.UclFixtures.Concat(teamData.UelFixtures).ToList();
            
            var recent = new List<ProcessedFixture>();
            var upcoming = new List<ProcessedFixture>();
            
            foreach (var fixture in allFixtures.Where(f => f.DateParsed.HasValue))
            {
                var daysDiff = (now - fixture.DateParsed!.Value).TotalDays;
                
                if (daysDiff >= -60 && daysDiff <= 0) // Upcoming (next 60 days)
                    upcoming.Add(fixture);
                else if (daysDiff > 0 && daysDiff <= 14) // Recent (last 14 days)
                    recent.Add(fixture);
            }
            
            allTeamFixtures[teamName] = teamData with
            {
                TotalEuropeanFixtures = allFixtures.Count,
                RecentMatches = recent.OrderByDescending(f => f.DateParsed).ToList(),
                UpcomingMatches = upcoming.OrderBy(f => f.DateParsed).ToList(),
                HasRecentEuropean = recent.Any(),
                HasUpcomingEuropean = upcoming.Any()
            };
        }
    }

    private async Task SaveRawFixturesAsync(string competitionCode, EuropeanFixturesApiResponse data, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_dataDirectory, $"{competitionCode.ToLower()}_fixtures.json");
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogDebug("Saved raw {Competition} fixtures to {FilePath}", competitionCode, filePath);
    }

    private async Task SaveTeamLookupAsync(Dictionary<string, TeamEuropeanFixtures> lookup, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_dataDirectory, "european_teams_lookup.json");
        var json = JsonSerializer.Serialize(lookup, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogInformation("Saved team lookup with {TeamCount} teams to {FilePath}", lookup.Count, filePath);
    }

    private async Task<Dictionary<string, TeamEuropeanFixtures>?> LoadTeamLookupAsync(CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_dataDirectory, "european_teams_lookup.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Team lookup file not found at {FilePath}", filePath);
            return null;
        }
        
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<Dictionary<string, TeamEuropeanFixtures>>(json);
    }
}

public class EuropeanFixturesOptions
{
    public string ApiHost { get; set; } = "free-api-live-football-data.p.rapidapi.com";
    public string ApiKey { get; set; } = string.Empty;
}
