using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

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
            var response = await client.GetFromJsonAsync<JsonElement>($"/fixtures?league={leagueId}&season={season}");
            
            if (!response.TryGetProperty("response", out var data))
                return fixtures;

            foreach (var item in data.EnumerateArray())
            {
                var fixture = item.GetProperty("fixture");
                var status = fixture.GetProperty("status");
                var teams = item.GetProperty("teams");
                var goals = item.GetProperty("goals");
                var score = item.GetProperty("score");
                
                var apiFixture = new ApiFixture(
                    ApiId: fixture.GetProperty("id").GetInt32(),
                    Date: DateTime.Parse(fixture.GetProperty("date").GetString() ?? DateTime.MinValue.ToString()),
                    StatusShort: status.GetProperty("short").GetString() ?? "",
                    HomeGoals: goals.GetProperty("home").ValueKind == JsonValueKind.Null ? null : goals.GetProperty("home").GetInt32(),
                    AwayGoals: goals.GetProperty("away").ValueKind == JsonValueKind.Null ? null : goals.GetProperty("away").GetInt32(),
                    HomeGoalsHalftime: GetHalftimeGoals(score, "home"),
                    AwayGoalsHalftime: GetHalftimeGoals(score, "away"),
                    HomeTeamApiId: teams.GetProperty("home").GetProperty("id").GetInt32(),
                    HomeTeamName: teams.GetProperty("home").GetProperty("name").GetString() ?? "",
                    AwayTeamApiId: teams.GetProperty("away").GetProperty("id").GetInt32(),
                    AwayTeamName: teams.GetProperty("away").GetProperty("name").GetString() ?? ""
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
            var response = await client.GetFromJsonAsync<JsonElement>($"/fixtures/statistics?fixture={fixtureId}");
            
            if (!response.TryGetProperty("response", out var data) || data.GetArrayLength() < 2)
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
            var response = await client.GetFromJsonAsync<JsonElement>($"/odds?fixture={fixtureId}");
            
            if (!response.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
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
            var response = await client.GetFromJsonAsync<JsonElement>(
                $"/standings?league={leagueId}&season={season}", cancellationToken);
            
            if (!response.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
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
    /// Test API connection
    /// </summary>
    public async Task<Dictionary<string, object>> TestConnectionAsync()
    {
        try
        {
            var response = await client.GetFromJsonAsync<JsonElement>("/status");
            
            if (response.TryGetProperty("response", out var data))
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
}
