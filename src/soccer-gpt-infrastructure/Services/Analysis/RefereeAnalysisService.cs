using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services.Analysis;

public class RefereeAnalysisService(IHistoricalDataRepository historyRepo, ILogger<RefereeAnalysisService> logger)
{
    // Cache referee stats: Name -> Review
    private Dictionary<string, RefereeStats>? _refereeDatabase;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<RefereeStats> AnalyzeRefereeAsync(string refereeName, DateTime matchDate)
    {
        if (string.IsNullOrWhiteSpace(refereeName)) 
            return RefereeStats.Default;

        await EnsureDatabaseLoadedAsync();

        if (_refereeDatabase != null && _refereeDatabase.TryGetValue(refereeName.Trim(), out var stats))
        {
            return stats;
        }

        return RefereeStats.Default;
    }

    private async Task EnsureDatabaseLoadedAsync()
    {
        if (_refereeDatabase != null) return;

        await _lock.WaitAsync();
        try
        {
            if (_refereeDatabase != null) return;

            logger.LogInformation("Building Referee Database from History...");
            var allMatches = await historyRepo.GetAllMatchesAsync();
            var stats = new Dictionary<string, RefereeBuilder>();

            // Aggregate stats
            foreach (var match in allMatches)
            {
                if (string.IsNullOrWhiteSpace(match.Referee)) continue;
                
                var refName = match.Referee.Trim();
                if (!stats.ContainsKey(refName)) stats[refName] = new RefereeBuilder();

                stats[refName].AddMatch(match);
            }

            // Finalize
            _refereeDatabase = stats.ToDictionary(
                k => k.Key, 
                v => v.Value.Build()
            );
            
            logger.LogInformation("Loaded {Count} Referees.", _refereeDatabase.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build referee database");
            _refereeDatabase = new Dictionary<string, RefereeStats>();
        }
        finally
        {
            _lock.Release();
        }
    }

    private class RefereeBuilder
    {
        private int Matches;
        private int TotalGoals;
        private int Over25;
        private int BTTS;
        
        public void AddMatch(HistoricalMatchDto match)
        {
            Matches++;
            var goals = match.FTHG + match.FTAG;
            TotalGoals += goals;
            if (goals > 2.5) Over25++;
            if (match.FTHG > 0 && match.FTAG > 0) BTTS++;
        }

        public RefereeStats Build()
        {
            if (Matches < 3) return RefereeStats.Default; // Not enough data
            
            return new RefereeStats
            {
                MatchesAnalyzed = Matches,
                AvgGoals = (double)TotalGoals / Matches,
                Over25Rate = (double)Over25 / Matches,
                BttsRate = (double)BTTS / Matches
            };
        }
    }
}

public record RefereeStats
{
    public int MatchesAnalyzed { get; init; }
    public double AvgGoals { get; init; }
    public double Over25Rate { get; init; }
    public double BttsRate { get; init; }

    public static RefereeStats Default => new() 
    { 
        MatchesAnalyzed = 0, 
        AvgGoals = 2.75, // League Avg Assumption
        Over25Rate = 0.52, 
        BttsRate = 0.52 
    };
}
