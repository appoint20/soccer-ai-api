using System.Globalization;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class FixtureService(ILogger<FixtureService> logger, ILeagueService leagueService) : IFixtureRepository
{
    private readonly string _fixturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "fixtures.csv");

    public async Task<List<UpcomingMatchDto>> GetFixturesAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        if (!File.Exists(_fixturePath))
        {
            logger.LogWarning("fixtures.csv not found at {Path}", _fixturePath);
            return [];
        }

        var matches = new List<UpcomingMatchDto>();

        try
        {
            using var reader = new StreamReader(_fixturePath);
            // Skipping header
            await reader.ReadLineAsync(cancellationToken);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                var div = parts[0];

                // Filter: Only include supported leagues
                if (!leagueService.IsLeagueSupported(div))
                {
                    continue; 
                }

                var date = parts[1];
                var time = parts[2];
                var home = parts[3];
                var away = parts[4];
                
                MatchOdds? odds = null;
                if (parts.Length > 8 && 
                    decimal.TryParse(parts[6], CultureInfo.InvariantCulture, out var hWins) &&
                    decimal.TryParse(parts[7], CultureInfo.InvariantCulture, out var draws) &&
                    decimal.TryParse(parts[8], CultureInfo.InvariantCulture, out var aWins))
                {
                    odds = new MatchOdds { HomeWin = hWins, Draw = draws, AwayWin = aWins };
                }

                matches.Add(new UpcomingMatchDto
                {
                    League = div, // Keep code for now as per previous state, lookup name when needed or mapped here? 
                                  // Let's adhere to "add mapping" request more strictly if needed, but previously we kept code.
                                  // User said "add mapping in fixtureRepository... is it best practise". 
                                  // Let's use the code here, handler logic dealt with folder mapping.
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

        // Apply pagination in memory (inefficient for specific CSVs but okay for small files)
        return matches.Skip(offset).Take(limit).ToList();
    }
    
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_fixturePath)) return 0;
        return (await File.ReadAllLinesAsync(_fixturePath, cancellationToken)).Length - 1; // Subtract header
    }
}
