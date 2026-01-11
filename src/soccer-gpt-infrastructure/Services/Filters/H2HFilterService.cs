using soccer_gpt_application.Models;
using soccer_gpt_application.Interfaces;
using System.Text.Json;

namespace soccer_gpt_infrastructure.Services.Filters;

public class H2HFilterService : IH2HFilterService
{
    private readonly HashSet<(string, string)> _derbyRivalries;

    public H2HFilterService()
    {
        _derbyRivalries = LoadDerbyRivalries();
    }

    public H2HAnalysisDto AnalyzeH2H(string homeTeam, string awayTeam, List<HistoricalMatchDto> allHistory)
    {
        var result = new H2HAnalysisDto();

        // 1. Derby Detection
        result.IsDerby = IsDerbyMatch(homeTeam, awayTeam);
        if (result.IsDerby) result.Tags.Add("Derby");

        // 2. Extract H2H matches (direct meetings, any venue, last 5)
        var h2hMatches = allHistory
            .Where(m => 
                (IsMatch(m.HomeTeam, homeTeam) && IsMatch(m.AwayTeam, awayTeam)) ||
                (IsMatch(m.HomeTeam, awayTeam) && IsMatch(m.AwayTeam, homeTeam)))
            .OrderByDescending(m => m.Date)
            .Take(5)
            .ToList();

        result.H2HMatchesCount = h2hMatches.Count;

        if (h2hMatches.Count < 3)
        {
            result.Tags.Add("Insufficient H2H Data");
            return result; // Not enough H2H data for pattern analysis
        }

        // 3. Analyze patterns
        foreach (var match in h2hMatches)
        {
            int totalGoals = match.FTHG + match.FTAG;
            bool btts = match is { FTHG: > 0, FTAG: > 0 };
            bool over25 = totalGoals > 2.5;
            bool is2to3 = totalGoals is 2 or 3;

            if (btts) result.BTTSCount++;
            if (over25) result.Over25Count++;
            if (is2to3) result.TwoToThreeGoalsCount++;

            // Determine winner from perspective of current fixture
            // If match was Home=A, Away=B and current fixture is Home=A, Away=B -> same
            // If match was Home=B, Away=A and current fixture is Home=A, Away=B -> reversed
            bool homeTeamWasHome = IsMatch(match.HomeTeam, homeTeam);
            
            if (homeTeamWasHome)
            {
                if (match.FTR == "H") result.HomeWins++;
                else if (match.FTR == "A") result.AwayWins++;
                else result.Draws++;
            }
            else // Home team was away in that H2H match
            {
                if (match.FTR == "H") result.AwayWins++;
                else if (match.FTR == "A") result.HomeWins++;
                else result.Draws++;
            }
        }

        // 4. Determine candidates (balanced criteria based on last 5)
        // Adjusted to be less aggressive - looking for strong patterns, not perfection
        int count = h2hMatches.Count;
        
        result.IsBTTSCandidate = result.BTTSCount >= 3; // 4+ of 5 (80%)
        result.IsOver25Candidate = result.Over25Count >= 3; // 4+ of 5 (80%)
        result.Is2to3GoalsCandidate = result.TwoToThreeGoalsCount >= 3; // 3+ of 5 (60%)
        result.IsHomeWinCandidate = result.HomeWins >= 3; // 3+ home wins (60%)
        result.IsAwayWinCandidate = result.AwayWins >= 3; // 3+ away wins (60%)
        result.IsDrawCandidate = result.Draws >= 4; // 3+ draws (60%)

        // 5. Add tags
        if (result.IsBTTSCandidate) result.Tags.Add($"BTTS Pattern ({result.BTTSCount}/{count})");
        if (result.IsOver25Candidate) result.Tags.Add($"Over 2.5 Pattern ({result.Over25Count}/{count})");
        if (result.Is2to3GoalsCandidate) result.Tags.Add($"2-3 Goals Pattern ({result.TwoToThreeGoalsCount}/{count})");
        if (result.IsHomeWinCandidate) result.Tags.Add($"Home Dominance ({result.HomeWins}/{count})");
        if (result.IsAwayWinCandidate) result.Tags.Add($"Away Dominance ({result.AwayWins}/{count})");
        if (result.IsDrawCandidate) result.Tags.Add($"Draw Pattern ({result.Draws}/{count})");

        return result;
    }

    private bool IsDerbyMatch(string homeTeam, string awayTeam)
    {
        var normalized1 = NormalizeTeamName(homeTeam);
        var normalized2 = NormalizeTeamName(awayTeam);
        
        return _derbyRivalries.Contains((normalized1, normalized2)) ||
               _derbyRivalries.Contains((normalized2, normalized1));
    }

    private HashSet<(string, string)> LoadDerbyRivalries()
    {
        var rivalries = new HashSet<(string, string)>();
        
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "derby_rivalries.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        string team1 = kvp.Key;
                        foreach (var team2 in kvp.Value)
                        {
                            rivalries.Add((team1, team2));
                        }
                    }
                }
            }
        }
        catch
        {
            // If file doesn't exist or can't be loaded, continue with empty set
        }

        return rivalries;
    }

    private string NormalizeTeamName(string teamName)
    {
        return teamName.Trim().ToLowerInvariant();
    }

    private bool IsMatch(string team1, string team2)
    {
        if (string.IsNullOrWhiteSpace(team1) || string.IsNullOrWhiteSpace(team2)) return false;
        return string.Equals(team1.Trim(), team2.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
