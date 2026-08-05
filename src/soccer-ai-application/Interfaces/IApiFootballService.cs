using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Fixture data returned from API for sync purposes
/// </summary>
public record ApiFixture(
    int ApiId,
    DateTimeOffset Date,
    string StatusShort,
    int? HomeGoals,
    int? AwayGoals,
    int? HomeGoalsHalftime,
    int? AwayGoalsHalftime,
    int HomeTeamApiId,
    string HomeTeamName,
    int AwayTeamApiId,
    string AwayTeamName,
    string? VenueSurface = null,
    string? VenueCity = null,
    double? Temp = null,
    int? Humidity = null,
    string? WeatherDesc = null);

/// <summary>
/// Betting odds from API
/// </summary>
public record FixtureOdds(
    double? HomeWin,
    double? Draw,
    double? AwayWin,
    double? Over25,
    double? Under25,
    double? BttsYes,
    double? BttsNo);

/// <summary>One bookmaker's price for one market outcome.</summary>
public record OddsQuote(string Bookmaker, string Market, double Price);

/// <summary>Canonical market keys for per-bookmaker quotes.</summary>
public static class OddsMarkets
{
    public const string HomeWin = "1x2_home";
    public const string Draw = "1x2_draw";
    public const string AwayWin = "1x2_away";
    public const string Over25 = "over25";
    public const string Under25 = "under25";
    public const string BttsYes = "btts_yes";
    public const string BttsNo = "btts_no";

    public static readonly string[] All =
        [HomeWin, Draw, AwayWin, Over25, Under25, BttsYes, BttsNo];
}

/// <summary>
/// Coach data from API
/// </summary>
public record TeamCoach(
    int Id,
    string Name,
    DateTimeOffset? Appointed);

/// <summary>Stats + red cards for one fixture, from a single batched response.</summary>
public record FixtureDetail(
    int FixtureId,
    FixtureStats? HomeStats,
    FixtureStats? AwayStats,
    int HomeRedCards,
    int AwayRedCards);

public interface IApiFootballService
{
    Task<List<ApiFixture>> GetFixturesAsync(int leagueId, int season);
    Task<(FixtureStats? Home, FixtureStats? Away)> GetBothTeamStatsAsync(int fixtureId);

    /// <summary>
    /// Batched fixture details: up to 20 fixtures per request via /fixtures?ids=,
    /// returning statistics AND events (red cards) in ONE call instead of two
    /// calls per fixture. Keyed by API fixture id.
    /// </summary>
    Task<Dictionary<int, FixtureDetail>> GetFixtureDetailsBatchAsync(
        IReadOnlyCollection<int> fixtureIds, CancellationToken ct = default);

    /// <summary>Whether the league+season provides odds (from /leagues coverage).</summary>
    Task<bool> HasOddsCoverageAsync(int leagueId, int season, CancellationToken ct = default);
    Task<FixtureOdds?> GetFixtureOddsAsync(int fixtureId);

    /// <summary>ALL bookmakers' prices for the supported markets (line shopping).</summary>
    Task<List<OddsQuote>> GetFixtureOddsQuotesAsync(int fixtureId);
    Task<List<Team>> GetStandingsAsync(int leagueId, int season, CancellationToken ct);
    Task<int?> GetLeagueIdByNameAsync(string leagueName, string country);
    Task<Dictionary<string, object>> TestConnectionAsync();
    
    // ── Contextual Intelligence Data ──
    Task<TeamCoach?> GetTeamCoachAsync(int teamId);
    Task<Dictionary<int, int>> GetFixtureRedCardsAsync(int fixtureId);
}


