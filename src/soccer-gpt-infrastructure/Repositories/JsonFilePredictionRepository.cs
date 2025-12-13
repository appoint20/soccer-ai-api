
using System.Text.Json;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Repositories;

public class JsonFilePredictionRepository(ILogger<JsonFilePredictionRepository> logger) : IPredictionRepository
{
    private readonly string _basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "predictions");

    public async Task<ApiFootballPrediction?> GetPredictionAsync(string leagueCode, string homeTeam, string awayTeam, string date, CancellationToken cancellationToken)
    {
        // date comes as DD/MM/YYYY from fixtures.csv usually.
        // We need YYYY-MM-DD for filename matching.
        if (!DateTime.TryParseExact(date, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var dt))
        {
            // Try fallback parsing if format differs
             if (!DateTime.TryParse(date, out dt))
             {
                 logger.LogWarning("Could not parse date {Date}", date);
                 return null;
             }
        }

        var folderName = MapLeagueToFolder(leagueCode);
        var dirPath = Path.Combine(_basePath, folderName);
        var searchDate = dt.ToString("yyyy-MM-dd");

        if (!Directory.Exists(dirPath))
        {
             logger.LogWarning("Prediction directory not found: {Path}", dirPath);
             return null;
        }

        // Find files starting with the date
        var files = Directory.GetFiles(dirPath, $"{searchDate}_*.json");
        
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var prediction = JsonSerializer.Deserialize<ApiFootballPrediction>(content);

                if (prediction != null && 
                    IsMatch(prediction.HomeTeam, homeTeam) && 
                    IsMatch(prediction.AwayTeam, awayTeam))
                {
                    return prediction;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading prediction file {File}", file);
            }
        }

        return null;
    }

    private static string MapLeagueToFolder(string code)
    {
         return code switch
        {
            "E0" => "Premier_League",
            "E1" => "Championship",
            "D1" => "Bundesliga",
            "I1" => "Serie_A",
            "SP1" => "La_Liga",
            "F1" => "Ligue_1",
            _ => "Premier_League"
        };
    }
    
    private static bool IsMatch(string s1, string s2)
    {
        if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;
        return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase) || 
               s1.Contains(s2, StringComparison.OrdinalIgnoreCase) || 
               s2.Contains(s1, StringComparison.OrdinalIgnoreCase);
    }
}
