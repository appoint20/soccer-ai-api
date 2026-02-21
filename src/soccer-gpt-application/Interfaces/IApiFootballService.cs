using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Fixture data returned from API for sync purposes
/// </summary>
public record ApiFixture(
    int ApiId,
    DateTime Date,
    string StatusShort,
    int? HomeGoals,
    int? AwayGoals,
    int? HomeGoalsHalftime,
    int? AwayGoalsHalftime,
    int HomeTeamApiId,
    string HomeTeamName,
    int AwayTeamApiId,
    string AwayTeamName);

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
    Task<Dictionary<string, object>> TestConnectionAsync();
}


