using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

public class ApiFootballService(HttpClient client, ILogger<ApiFootballService> logger) : IApiFootballService
{
    /// <summary>
    /// Fetch completed fixtures for a league/season
    /// </summary>
    public async Task<List<ApiFixture>> GetFixturesAsync(int leagueId, int season)
    {
        var fixtures = new List<ApiFixture>();
        
        try
        {
            var response = await GetApiResponseAsync($"/fixtures?league={leagueId}&season={season}");
            if (response is null)
                return fixtures;
            
            if (!response.Value.TryGetProperty("response", out var data))
                return fixtures;

            foreach (var item in data.EnumerateArray())
            {
                var fixture = item.GetProperty("fixture");
                var status = fixture.GetProperty("status");
                var teams = item.GetProperty("teams");
                var goals = item.GetProperty("goals");
                var score = item.GetProperty("score");
                
                // Venue Details
                fixture.TryGetProperty("venue", out var venue);
                var venueSurface = venue.ValueKind != JsonValueKind.Null && venue.TryGetProperty("surface", out var vs) ? vs.GetString() : null;
                var venueCity = venue.ValueKind != JsonValueKind.Null && venue.TryGetProperty("city", out var vc) ? vc.GetString() : null;

                // Weather Details
                double? temp = null;
                int? humidity = null;
                string? weatherDesc = null;
                if (item.TryGetProperty("fixture", out var f) && f.TryGetProperty("weather", out var weather))
                {
                    temp = weather.TryGetProperty("temp", out var t) && t.ValueKind != JsonValueKind.Null ? t.GetDouble() : null;
                    humidity = weather.TryGetProperty("humidity", out var h) && h.ValueKind != JsonValueKind.Null ? h.GetInt32() : null;
                    weatherDesc = weather.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null;
                }

                var apiFixture = new ApiFixture(
                    ApiId: fixture.GetProperty("id").GetInt32(),
                    Date: DateTimeOffset.Parse(fixture.GetProperty("date").GetString() ?? DateTimeOffset.UtcNow.ToString("O")),
                    StatusShort: status.GetProperty("short").GetString() ?? "",
                    HomeGoals: goals.GetProperty("home").ValueKind == JsonValueKind.Null ? null : goals.GetProperty("home").GetInt32(),
                    AwayGoals: goals.GetProperty("away").ValueKind == JsonValueKind.Null ? null : goals.GetProperty("away").GetInt32(),
                    HomeGoalsHalftime: GetHalftimeGoals(score, "home"),
                    AwayGoalsHalftime: GetHalftimeGoals(score, "away"),
                    HomeTeamApiId: teams.GetProperty("home").GetProperty("id").GetInt32(),
                    HomeTeamName: teams.GetProperty("home").GetProperty("name").GetString() ?? "",
                    AwayTeamApiId: teams.GetProperty("away").GetProperty("id").GetInt32(),
                    AwayTeamName: teams.GetProperty("away").GetProperty("name").GetString() ?? "",
                    VenueSurface: venueSurface,
                    VenueCity: venueCity,
                    Temp: temp,
                    Humidity: humidity,
                    WeatherDesc: weatherDesc
                );
                
                fixtures.Add(apiFixture);
            }
            
            logger.LogInformation("Fetched {Count} fixtures for league {LeagueId}", fixtures.Count, leagueId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch fixtures for league {LeagueId}", leagueId);
        }
        
        return fixtures;
    }

    private static int? GetHalftimeGoals(JsonElement score, string team)
    {
        if (!score.TryGetProperty("halftime", out var ht)) return null;
        if (!ht.TryGetProperty(team, out var val)) return null;
        return val.ValueKind == JsonValueKind.Null ? null : val.GetInt32();
    }

    /// <summary>
    /// Get statistics for both teams in a fixture
    /// </summary>
    public async Task<(FixtureStats? Home, FixtureStats? Away)> GetBothTeamStatsAsync(int fixtureId)
    {
        try
        {
            var response = await GetApiResponseAsync($"/fixtures/statistics?fixture={fixtureId}");
            if (response is null)
                return (null, null);
            
            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() < 2)
                return (null, null);

            var homeStats = ParseTeamStats(data[0]);
            var awayStats = ParseTeamStats(data[1]);
            
            return (homeStats, awayStats);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch stats for fixture {FixtureId}", fixtureId);
            return (null, null);
        }
    }

    private static FixtureStats? ParseTeamStats(JsonElement teamData)
    {
        if (!teamData.TryGetProperty("statistics", out var stats))
            return null;

        int GetInt(string type)
        {
            foreach (var s in stats.EnumerateArray())
            {
                if (s.GetProperty("type").GetString() == type)
                {
                    var val = s.GetProperty("value");
                    if (val.ValueKind == JsonValueKind.Number)
                        return val.GetInt32();
                    if (val.ValueKind == JsonValueKind.String)
                    {
                        var str = val.GetString()?.Replace("%", "") ?? "0";
                        return int.TryParse(str, out var n) ? n : 0;
                    }
                }
            }
            return 0;
        }

        double? GetDouble(string type)
        {
            foreach (var s in stats.EnumerateArray())
            {
                if (s.GetProperty("type").GetString() == type)
                {
                    var val = s.GetProperty("value");
                    if (val.ValueKind == JsonValueKind.Number)
                        return val.GetDouble();
                    if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(), out var d))
                        return d;
                }
            }
            return null;
        }

        return new FixtureStats
        {
            TotalShots = GetInt("Total Shots"),
            ShotsOnGoal = GetInt("Shots on Goal"),
            BallPossession = GetInt("Ball Possession"),
            PassesAccurate = GetInt("Passes accurate"),
            ExpectedGoals = GetDouble("expected_goals")
        };
    }

    /// <summary>
    /// Fetch betting odds for a fixture
    /// </summary>
    public async Task<FixtureOdds?> GetFixtureOddsAsync(int fixtureId)
    {
        try
        {
            var response = await GetApiResponseAsync($"/odds?fixture={fixtureId}");
            if (response is null)
                return null;
            
            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
                return null;

            var bookmakers = data[0].GetProperty("bookmakers");
            if (bookmakers.GetArrayLength() == 0) return null;

            // Try to find Bet365 first, then fallback to first bookmaker
            JsonElement? targetBookmaker = null;
            foreach (var bm in bookmakers.EnumerateArray())
            {
                if (bm.GetProperty("name").GetString() == "Bet365")
                {
                    targetBookmaker = bm;
                    break;
                }
            }
            targetBookmaker ??= bookmakers[0];

            double? homeWin = null, draw = null, awayWin = null;
            double? over25 = null, under25 = null;
            double? bttsYes = null, bttsNo = null;

            foreach (var bet in targetBookmaker.Value.GetProperty("bets").EnumerateArray())
            {
                var betName = bet.GetProperty("name").GetString();
                var values = bet.GetProperty("values");

                if (betName == "Match Winner")
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        var val = v.GetProperty("value").GetString();
                        var odd = double.TryParse(v.GetProperty("odd").GetString(), out var o) ? o : (double?)null;
                        if (val == "Home") homeWin = odd;
                        else if (val == "Draw") draw = odd;
                        else if (val == "Away") awayWin = odd;
                    }
                }
                else if (betName == "Goals Over/Under" || betName == "Over/Under 2.5")
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        var val = v.GetProperty("value").GetString();
                        var odd = double.TryParse(v.GetProperty("odd").GetString(), out var o) ? o : (double?)null;
                        if (val == "Over 2.5") over25 = odd;
                        else if (val == "Under 2.5") under25 = odd;
                    }
                }
                else if (betName == "Both Teams Score")
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        var val = v.GetProperty("value").GetString();
                        var odd = double.TryParse(v.GetProperty("odd").GetString(), out var o) ? o : (double?)null;
                        if (val == "Yes") bttsYes = odd;
                        else if (val == "No") bttsNo = odd;
                    }
                }
            }

            return new FixtureOdds(homeWin, draw, awayWin, over25, under25, bttsYes, bttsNo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch odds for fixture {FixtureId}", fixtureId);
            return null;
        }
    }

    /// <summary>
    /// Fetch current standings for a league/season
    /// </summary>
    public async Task<List<Team>> GetStandingsAsync(int leagueId, int season, CancellationToken cancellationToken)
    {
        var teams = new List<Team>();
        
        try
        {
            var response = await GetApiResponseAsync(
                $"/standings?league={leagueId}&season={season}",
                cancellationToken);
            if (response is null)
                return teams;
            
            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
            {
                logger.LogWarning("No standings data for league {LeagueId} season {Season}", leagueId, season);
                return teams;
            }

            var leagueData = data[0].GetProperty("league");
            var standings = leagueData.GetProperty("standings");
            
            if (standings.GetArrayLength() == 0) return teams;
            
            var standingsGroup = standings[0];

            foreach (var item in standingsGroup.EnumerateArray())
            {
                var teamInfo = item.GetProperty("team");
                var all = item.GetProperty("all");
                var goals = all.GetProperty("goals");
                
                teams.Add(new Team
                {
                    ApiId = teamInfo.GetProperty("id").GetInt32(),
                    Name = teamInfo.GetProperty("name").GetString() ?? "Unknown",
                    ShortName = teamInfo.TryGetProperty("code", out var code) && code.ValueKind != JsonValueKind.Null ? code.GetString() : null,
                    LeagueId = leagueId,
                    Rank = item.GetProperty("rank").GetInt32(),
                    Points = item.GetProperty("points").GetInt32(),
                    GoalsFor = goals.GetProperty("for").GetInt32(),
                    GoalsAgainst = goals.GetProperty("against").GetInt32(),
                    GoalsDiff = item.GetProperty("goalsDiff").GetInt32(),
                    Played = all.GetProperty("played").GetInt32(),
                    Win = all.GetProperty("win").GetInt32(),
                    Draw = all.GetProperty("draw").GetInt32(),
                    Lose = all.GetProperty("lose").GetInt32(),
                    Form = item.GetProperty("form").GetString() ?? "",
                    UpdatedAt = DateTime.UtcNow
                });
            }
            
            logger.LogInformation("Fetched {Count} team standings for league {LeagueId}", teams.Count, leagueId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch standings for league {LeagueId}", leagueId);
        }
        
        return teams;
    }

    /// <summary>
    /// Fetch league ID by name and country
    /// </summary>
    public async Task<int?> GetLeagueIdByNameAsync(string leagueName, string country)
    {
        try
        {
            logger.LogInformation("Searching for league '{LeagueName}' in '{Country}'...", leagueName, country);
            var response = await GetApiResponseAsync($"/leagues?search={leagueName}&country={country}");
            
            // If specific search fails, try searching just by name and filtering in code
            if (response == null || !response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
            {
                logger.LogInformation("Specific country search failed. Trying broad search for '{LeagueName}'...", leagueName);
                response = await GetApiResponseAsync($"/leagues?search={leagueName}");
            }

            if (response is null)
                return null;

            if (!response.Value.TryGetProperty("response", out var leaguesData) || leaguesData.GetArrayLength() == 0)
            {
                logger.LogWarning("No league found for '{LeagueName}'", leagueName);
                return null;
            }

            // Find the best match
            foreach (var item in leaguesData.EnumerateArray())
            {
                var league = item.GetProperty("league");
                var countryInfo = item.GetProperty("country");
                var name = league.GetProperty("name").GetString();
                var cName = countryInfo.GetProperty("name").GetString();
                var id = league.GetProperty("id").GetInt32();
                
                logger.LogInformation("Found League: {Name} (ID: {Id}) in {Country}", name, id, cName);

                if (name != null && name.Contains(leagueName, StringComparison.OrdinalIgnoreCase) && 
                    cName != null && cName.Equals(country, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Resolved League ID {Id} for {LeagueName} in {Country}", id, leagueName, country);
                    return id;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve league ID for {LeagueName}", leagueName);
            return null;
        }
    }

    /// <summary>
    /// Test API connection
    /// </summary>
    public async Task<Dictionary<string, object>> TestConnectionAsync()
    {
        try
        {
            var response = await GetApiResponseAsync("/status");
            if (response is null)
            {
                return new Dictionary<string, object>
                {
                    ["status"] = "error",
                    ["message"] = "No response or non-success status from API-Football."
                };
            }
            
            if (response.Value.TryGetProperty("response", out var data))
            {
                var account = data.GetProperty("account");
                var subscription = data.GetProperty("subscription");
                var requests = data.GetProperty("requests");
                
                return new Dictionary<string, object>
                {
                    ["status"] = "connected",
                    ["account"] = account.GetProperty("firstname").GetString() ?? "",
                    ["plan"] = subscription.GetProperty("plan").GetString() ?? "",
                    ["requests_today"] = requests.GetProperty("current").GetInt32(),
                    ["requests_limit"] = requests.GetProperty("limit_day").GetInt32()
                };
            }
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["status"] = "error",
                ["message"] = ex.Message
            };
        }
        
        return new Dictionary<string, object> { ["status"] = "unknown" };
    }

    private async Task<JsonElement?> GetApiResponseAsync(string relativeUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetAsync(relativeUrl, ct);

            // Rate-limit detection
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("API-Football rate limit exceeded for {Url}", relativeUrl);
                throw Application.Exceptions.ExternalApiException.RateLimited("API-Football");
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "API-Football HTTP error. Url: {Url}, Status: {Status}, Body: {Body}",
                    relativeUrl,
                    (int)response.StatusCode,
                    TrimForLog(body));
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (Application.Exceptions.ExternalApiException)
        {
            throw; // Re-throw typed exceptions
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogWarning("API-Football request timed out for {Url}", relativeUrl);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "API-Football network error for {Url}", relativeUrl);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "API-Football response parse failure for {Url}", relativeUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API-Football unexpected error for {Url}", relativeUrl);
            return null;
        }
    }

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        const int max = 500;
        return value.Length <= max ? value : value[..max];
    }
}
