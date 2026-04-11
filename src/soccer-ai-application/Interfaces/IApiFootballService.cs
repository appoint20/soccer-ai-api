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

public interface IApiFootballService
{
    Task<List<ApiFixture>> GetFixturesAsync(int leagueId, int season);
    Task<(FixtureStats? Home, FixtureStats? Away)> GetBothTeamStatsAsync(int fixtureId);
    Task<FixtureOdds?> GetFixtureOddsAsync(int fixtureId);
    Task<List<Team>> GetStandingsAsync(int leagueId, int season, CancellationToken ct);
    Task<int?> GetLeagueIdByNameAsync(string leagueName, string country);
    Task<Dictionary<string, object>> TestConnectionAsync();
}


