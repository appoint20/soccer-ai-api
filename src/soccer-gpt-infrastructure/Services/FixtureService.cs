using System.Globalization;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class FixtureService(ILogger<FixtureService> logger, ILeagueService leagueService) : IFixtureRepository
{
    private readonly string _fixturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "upcoming", "fixtures.csv");

    public async Task<List<UpcomingMatchDto>> GetFixturesAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        logger.LogWarning("Reading Fixtures from: {Path}", _fixturePath);
        if (!File.Exists(_fixturePath))
        {
            logger.LogWarning("fixtures.csv not found at {Path}", _fixturePath);
            return [];
        }

        var matches = new List<UpcomingMatchDto>();

        try
        {
            using var reader = new StreamReader(_fixturePath);
            // Read Header
            var headerLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(headerLine)) return [];
            
            var headers = headerLine.Split(',');
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                colMap[headers[i].Trim()] = i;
            }

            // Helper to get value
            string? GetVal(string[] row, string colName, params string[] alts)
            {
                if (colMap.TryGetValue(colName, out var idx) && idx < row.Length) return row[idx];
                foreach (var alt in alts)
                {
                    if (colMap.TryGetValue(alt, out var altIdx) && altIdx < row.Length) return row[altIdx];
                }
                return null;
            }

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                // Simple CSV split (assuming no commas in fields for now, typical for football-data.co.uk)
                var parts = line.Split(',');
                
                var div = GetVal(parts, "Div");
                if (string.IsNullOrEmpty(div)) continue;

                if (!leagueService.IsLeagueSupported(div)) continue;

                var date = GetVal(parts, "Date") ?? "";
                var time = GetVal(parts, "Time") ?? "";
                var home = GetVal(parts, "HomeTeam") ?? "";
                var away = GetVal(parts, "AwayTeam") ?? "";
                
                // Parse Odds
                MatchOdds? odds = null;
                
                // Try B365, then Avg, then Max
                var hStr = GetVal(parts, "B365H", "AvgH", "MaxH");
                var dStr = GetVal(parts, "B365D", "AvgD", "MaxD");
                var aStr = GetVal(parts, "B365A", "AvgA", "MaxA");
                
                var overStr = GetVal(parts, "B365>2.5", "Avg>2.5", "Max>2.5");
                var underStr = GetVal(parts, "B365<2.5", "Avg<2.5", "Max<2.5");

                if (decimal.TryParse(hStr, CultureInfo.InvariantCulture, out var h) &&
                    decimal.TryParse(dStr, CultureInfo.InvariantCulture, out var d) &&
                    decimal.TryParse(aStr, CultureInfo.InvariantCulture, out var a))
                {
                     odds = new MatchOdds 
                     { 
                         HomeWin = h, 
                         Draw = d, 
                         AwayWin = a 
                     };
                     
                     if (decimal.TryParse(overStr, CultureInfo.InvariantCulture, out var o)) odds = odds with { Over25 = o };
                     if (decimal.TryParse(underStr, CultureInfo.InvariantCulture, out var u)) odds = odds with { Under25 = u };
                }

                matches.Add(new UpcomingMatchDto
                {
                    League = div,
                    Date = date,
                    Time = time,
                    HomeTeam = home,
                    AwayTeam = away,
                    Odds = odds
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading fixtures.csv");
        }

        return matches.Skip(offset).Take(limit).ToList();
    }
    
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_fixturePath)) return 0;
        return (await File.ReadAllLinesAsync(_fixturePath, cancellationToken)).Length - 1; // Subtract header
    }
}
