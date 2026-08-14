using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

public class ApiFootballService(
    HttpClient client,
    IApiQuotaTracker quota,
    IApiCallTracker calls,
    ILogger<ApiFootballService> logger) : IApiFootballService
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
        catch (Application.Exceptions.ExternalApiException)
        {
            throw; // Rate limit or rejected key: abort the run, do not report success.
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
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch stats for fixture {FixtureId}", fixtureId);
            return (null, null);
        }
    }

    /// <summary>Max fixture ids per /fixtures?ids= request (API-Football limit).</summary>
    public const int MaxFixtureIdsPerBatch = 20;

    public async Task<Dictionary<int, FixtureDetail>> GetFixtureDetailsBatchAsync(
        IReadOnlyCollection<int> fixtureIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, FixtureDetail>();
        if (fixtureIds.Count == 0) return result;

        foreach (var batch in fixtureIds.Distinct().Chunk(MaxFixtureIdsPerBatch))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ids = string.Join('-', batch);
                var response = await GetApiResponseAsync($"/fixtures?ids={ids}", ct);
                if (response is null) continue;

                if (!response.Value.TryGetProperty("response", out var data)) continue;

                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("fixture", out var fx) ||
                        !fx.TryGetProperty("id", out var idEl)) continue;

                    var fixtureId = idEl.GetInt32();
                    var homeTeamId = item.GetProperty("teams").GetProperty("home").GetProperty("id").GetInt32();

                    // ── statistics (present when the plan/coverage includes them) ──
                    FixtureStats? home = null, away = null;
                    if (item.TryGetProperty("statistics", out var statsArray) &&
                        statsArray.ValueKind == JsonValueKind.Array &&
                        statsArray.GetArrayLength() >= 2)
                    {
                        home = ParseTeamStats(statsArray[0]);
                        away = ParseTeamStats(statsArray[1]);
                    }

                    // ── events → red cards per side ──
                    int homeRed = 0, awayRed = 0;
                    if (item.TryGetProperty("events", out var events) &&
                        events.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ev in events.EnumerateArray())
                        {
                            if (!ev.TryGetProperty("detail", out var detailEl)) continue;
                            var detail = detailEl.GetString() ?? "";
                            if (!detail.Contains("Red Card", StringComparison.OrdinalIgnoreCase)) continue;

                            var teamId = ev.GetProperty("team").GetProperty("id").GetInt32();
                            if (teamId == homeTeamId) homeRed++;
                            else awayRed++;
                        }
                    }

                    result[fixtureId] = new FixtureDetail(fixtureId, home, away, homeRed, awayRed);
                }
            }
            catch (Application.Exceptions.ExternalApiException)
            {
                throw; // rate limit — let the caller abort cleanly
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Batched fixture detail fetch failed for {Count} ids", batch.Length);
            }
        }

        logger.LogInformation(
            "[ApiBatch] Fetched details for {Found}/{Requested} fixtures in {Calls} call(s) (was {Old} calls)",
            result.Count, fixtureIds.Count,
            (int)Math.Ceiling(fixtureIds.Count / (double)MaxFixtureIdsPerBatch), fixtureIds.Count * 2);

        return result;
    }

    /// <summary>
    /// Coverage check: does this league+season actually provide odds?
    /// Prevents pointless /odds calls for leagues the API never prices.
    /// </summary>
    public async Task<bool> HasOddsCoverageAsync(int leagueId, int season, CancellationToken ct = default)
    {
        try
        {
            var response = await GetApiResponseAsync($"/leagues?id={leagueId}&season={season}", ct);
            if (response is null) return true; // unknown → don't block syncing

            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
                return true;

            foreach (var seasonEl in data[0].GetProperty("seasons").EnumerateArray())
            {
                if (seasonEl.GetProperty("year").GetInt32() != season) continue;
                if (!seasonEl.TryGetProperty("coverage", out var coverage)) return true;
                if (!coverage.TryGetProperty("odds", out var odds)) return true;
                return odds.ValueKind != JsonValueKind.False;
            }

            return true;
        }
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Coverage check failed for league {LeagueId} season {Season}", leagueId, season);
            return true; // fail open
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
                        return int.TryParse(str, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
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
                    if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
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
        var quotes = await GetFixtureOddsQuotesAsync(fixtureId);
        if (quotes.Count == 0) return null;
        return SoccerAi.Application.Services.OddsQuoteAggregator.BestPrices(quotes);
    }

    /// <summary>
    /// ALL bookmakers' prices for 1X2, Over/Under 2.5 and BTTS.
    /// Previously only Bet365 (or the first bookmaker) was read — one missing
    /// bookmaker meant no odds at all. Line shopping across every listed
    /// bookmaker both raises coverage and gives the best available price.
    /// </summary>
    public async Task<List<OddsQuote>> GetFixtureOddsQuotesAsync(int fixtureId)
    {
        var quotes = new List<OddsQuote>();
        try
        {
            var response = await GetApiResponseAsync($"/odds?fixture={fixtureId}");
            if (response is null)
                return quotes;

            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
                return quotes;

            var bookmakers = data[0].GetProperty("bookmakers");

            foreach (var bm in bookmakers.EnumerateArray())
            {
                var bookmaker = bm.GetProperty("name").GetString() ?? "unknown";

                foreach (var bet in bm.GetProperty("bets").EnumerateArray())
                {
                    var betName = bet.GetProperty("name").GetString();
                    var values = bet.GetProperty("values");

                    switch (betName)
                    {
                        case "Match Winner":
                            AddQuotes(quotes, bookmaker, values,
                                ("Home", OddsMarkets.HomeWin),
                                ("Draw", OddsMarkets.Draw),
                                ("Away", OddsMarkets.AwayWin));
                            break;
                        case "Goals Over/Under":
                        case "Over/Under 2.5":
                            AddQuotes(quotes, bookmaker, values,
                                ("Over 2.5", OddsMarkets.Over25),
                                ("Under 2.5", OddsMarkets.Under25));
                            break;
                        case "Both Teams Score":
                            AddQuotes(quotes, bookmaker, values,
                                ("Yes", OddsMarkets.BttsYes),
                                ("No", OddsMarkets.BttsNo));
                            break;
                    }
                }
            }
        }
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch odds quotes for fixture {FixtureId}", fixtureId);
        }
        return quotes;
    }

    private static void AddQuotes(
        List<OddsQuote> quotes, string bookmaker, JsonElement values,
        params (string ApiValue, string Market)[] mapping)
    {
        foreach (var v in values.EnumerateArray())
        {
            var val = v.GetProperty("value").GetString();
            var match = mapping.FirstOrDefault(m => m.ApiValue == val);
            if (match.Market is null) continue;

            if (double.TryParse(v.GetProperty("odd").GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var odd))
            {
                quotes.Add(new OddsQuote(bookmaker, match.Market, odd));
            }
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
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
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

    /// <summary>
    /// Fetch current coach for a team
    /// </summary>
    public async Task<TeamCoach?> GetTeamCoachAsync(int teamId)
    {
        try
        {
            var response = await GetApiResponseAsync($"/coachs?team={teamId}");
            if (response is null) return null;
            
            if (!response.Value.TryGetProperty("response", out var data) || data.GetArrayLength() == 0)
                return null;

            // The API returns a list of coaches, usually the current one has "career" entry where "end" is null.
            // Or we just take the first coach in the response if they only return the current one.
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt32();
                var name = item.GetProperty("name").GetString() ?? "";
                
                // Let's try to find the appointment date from career
                DateTimeOffset? appointed = null;
                if (item.TryGetProperty("career", out var career) && career.GetArrayLength() > 0)
                {
                    // Find the career entry for the current team where end is null
                    foreach (var c in career.EnumerateArray())
                    {
                        var team = c.GetProperty("team").GetProperty("id").GetInt32();
                        var end = c.GetProperty("end").ValueKind == JsonValueKind.Null ? (string?)null : c.GetProperty("end").GetString();
                        
                        if (team == teamId && end == null)
                        {
                            var startStr = c.GetProperty("start").GetString();
                            if (DateTimeOffset.TryParse(startStr, out var start))
                                appointed = start;
                            break;
                        }
                    }
                }
                
                return new TeamCoach(id, name, appointed);
            }
        }
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch coach for team {TeamId}", teamId);
        }
        return null;
    }

    /// <summary>
    /// Fetch red cards for a specific fixture
    /// </summary>
    public async Task<Dictionary<int, int>> GetFixtureRedCardsAsync(int fixtureId)
    {
        var redCards = new Dictionary<int, int>();
        try
        {
            var response = await GetApiResponseAsync($"/fixtures/events?fixture={fixtureId}&type=Card");
            if (response is null) return redCards;
            
            if (!response.Value.TryGetProperty("response", out var data))
                return redCards;

            foreach (var item in data.EnumerateArray())
            {
                var detail = item.GetProperty("detail").GetString() ?? "";
                if (detail.Contains("Red Card", StringComparison.OrdinalIgnoreCase))
                {
                    var teamId = item.GetProperty("team").GetProperty("id").GetInt32();
                    if (!redCards.ContainsKey(teamId))
                        redCards[teamId] = 0;
                    redCards[teamId]++;
                }
            }
        }
        catch (Application.Exceptions.ExternalApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch red cards for fixture {FixtureId}", fixtureId);
        }
        return redCards;
    }

    private async Task<JsonElement?> GetApiResponseAsync(string relativeUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetAsync(relativeUrl, ct);

            // Quota headers first: they tell us how close we are BEFORE a 429.
            quota.Update(name =>
                response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null);

            // Rate-limit detection
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("API-Football rate limit exceeded for {Url}", relativeUrl);
                calls.RecordFailure("Rate limit exceeded");
                throw Application.Exceptions.ExternalApiException.RateLimited("API-Football");
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // A rejected key is a configuration fault, not a transient one. It
            // will fail identically for every remaining league, so stop the run
            // here instead of logging the same 403 thirty more times and then
            // reporting the sync as successful.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogError(
                    "API-Football rejected the API key. Url: {Url}, Status: {Status}, Body: {Body}",
                    relativeUrl, (int)response.StatusCode, TrimForLog(body));

                calls.RecordFailure($"API key rejected ({(int)response.StatusCode})");
                throw Application.Exceptions.ExternalApiException.Unauthorized(
                    "API-Football", response.StatusCode, TrimForLog(body));
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "API-Football HTTP error. Url: {Url}, Status: {Status}, Body: {Body}",
                    relativeUrl,
                    (int)response.StatusCode,
                    TrimForLog(body));
                calls.RecordFailure($"HTTP {(int)response.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            calls.RecordSuccess();
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
