using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class PredictionWorkerService(
    ILogger<PredictionWorkerService> logger,
    IFootballApiService footballApiService,
    ILeaguesRepository leaguesRepository) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);
    private readonly string _predictionsBasePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data", "predictions");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Prediction Worker Service is starting.");

        // Initial delay to let app startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPredictionsAsync(stoppingToken);
            
            logger.LogInformation("Prediction cycle complete. Waiting {Hours} hours...", _checkInterval.TotalHours);
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task ProcessPredictionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting daily prediction fetch cycle...");

            var leagues = await leaguesRepository.GetLeaguesAsync(cancellationToken);
            if (leagues == null || leagues.Count == 0)
            {
                logger.LogWarning("No leagues found configured.");
                return;
            }

            foreach (var league in leagues.TakeWhile(league => !cancellationToken.IsCancellationRequested))
            {
                logger.LogInformation("Processing league: {League} (ID: {Id})", league.Name, league.ApiId);

                // Fetch next 10 fixtures for this league
                // Assuming current season is 2025 based on previous context/files
                int season = 2025; 
                var fixtures = await footballApiService.GetFixturesAsync(league.ApiId, season, 10, cancellationToken);
                
                if (fixtures == null || fixtures.Count == 0)
                {
                    logger.LogInformation("No upcoming fixtures found for {League}", league.Name);
                    continue;
                }

                foreach (var fixtureItem in fixtures)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    int fixtureId = fixtureItem.Fixture.Id;
                    
                    // Check if we already have a FRESH prediction for this fixture (less than 24h old)
                    if (IsPredictionCached(league.FolderName, fixtureItem.Fixture.Date, fixtureId)) // Pass Date string to helper?
                    {
                        // Skipping to save API calls if fresh
                        continue;
                    }

                    // Fetch Prediction
                    var prediction = await footballApiService.GetPredictionAsync(fixtureId, cancellationToken);
                    if (prediction != null)
                    {
                        await SavePredictionAsync(league.FolderName, fixtureItem.Fixture.Date, fixtureId, prediction, cancellationToken);
                    }
                    
                    // Rate limit protection
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Prediction Worker Service");
        }
    }

    private bool IsPredictionCached(string leagueFolder, string matchDateIso, int fixtureId)
    {
        // Filename format: YYYY-MM-DD_{FixtureId}.json
        // Matches existing format seen in file list
        // Date parsing: 2025-12-14T15:00:00+00:00 -> 2025-12-14
        if (!DateTime.TryParse(matchDateIso, out var date)) return false;
        
        var dateStr = date.ToString("yyyy-MM-dd");
        var filename = $"{dateStr}_{fixtureId}.json";
        var filePath = Path.Combine(_predictionsBasePath, leagueFolder, filename);

        if (!File.Exists(filePath)) return false;

        var info = new FileInfo(filePath);
        // Cache valid for 24 hours
        return (DateTime.UtcNow - info.LastWriteTimeUtc).TotalHours < 24;
    }

    private async Task SavePredictionAsync(string leagueFolder, string matchDateIso, int fixtureId, ApiFootballPrediction prediction, CancellationToken token)
    {
        if (!DateTime.TryParse(matchDateIso, out var date)) return;
        var dateStr = date.ToString("yyyy-MM-dd");
        
        var folderPath = Path.Combine(_predictionsBasePath, leagueFolder);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var filename = $"{dateStr}_{fixtureId}.json";
        var filePath = Path.Combine(folderPath, filename);

        // Add metadata about fetch time? wrapping or just saving raw?
        // Let's create a wrapper or just save the object as is since it's the expected format for the repository
        // The repository reads ApiFootballPrediction.
        
        // Add fetch timestamp property or wrapper?
        // The existing file structure seemed to just be the JSON content.
        
        var json = JsonSerializer.Serialize(prediction, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, token);
        
        logger.LogInformation("Saved prediction for Fixture {Id} to {Path}", fixtureId, filename);
    }
}
