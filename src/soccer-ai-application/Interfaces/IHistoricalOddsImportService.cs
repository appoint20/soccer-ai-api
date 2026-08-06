namespace SoccerAi.Application.Interfaces;

/// <summary>Outcome for one league-season file.</summary>
public sealed record HistoricalOddsSeasonResult(
    int LeagueId,
    string Division,
    string Season,
    int CsvRows,
    int FixturesMatched,
    int FixturesPriced,
    int AlreadyPriced,
    int FixtureNotFound,
    IReadOnlyList<string> UnmatchedTeamNames,
    string? Error = null);

public sealed record HistoricalOddsImportResult(
    IReadOnlyList<HistoricalOddsSeasonResult> Seasons)
{
    public int CsvRows => Seasons.Sum(s => s.CsvRows);
    public int FixturesPriced => Seasons.Sum(s => s.FixturesPriced);
    public int AlreadyPriced => Seasons.Sum(s => s.AlreadyPriced);
    public int FixtureNotFound => Seasons.Sum(s => s.FixtureNotFound);

    /// <summary>Every external name that could not be resolved, deduplicated.</summary>
    public IReadOnlyList<string> UnmatchedTeamNames =>
        [.. Seasons.SelectMany(s => s.UnmatchedTeamNames).Distinct().Order()];
}

/// <summary>
/// One-time import of historical Bet365 prices from football-data.co.uk.
///
/// Fills the gap API-Football cannot: it retains only seven days of pre-match
/// odds, leaving most finished fixtures unpriced and therefore invisible to the
/// value gate — which is why the backtest measures 26 picks rather than
/// hundreds.
///
/// Import rules: only real published prices are written, only onto fixtures
/// that have none, and an unresolved team name is reported rather than guessed.
/// </summary>
public interface IHistoricalOddsImportService
{
    Task<HistoricalOddsImportResult> ImportAsync(
        IReadOnlyCollection<int> seasons, bool dryRun, CancellationToken ct = default);
}
