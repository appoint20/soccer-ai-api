using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for ingesting enriched fixtures from API + historical data
/// </summary>
public interface IEnrichedFixtureIngestionService
{
    /// <summary>
    /// Ingest fixtures for all English leagues for a given season
    /// </summary>
    Task<IngestionResult> IngestEnglishLeaguesAsync(int season);

    /// <summary>
    /// Ingest fixtures for a specific league and season
    /// </summary>
    Task<IngestionResult> IngestLeagueAsync(int leagueId, int season);
}
