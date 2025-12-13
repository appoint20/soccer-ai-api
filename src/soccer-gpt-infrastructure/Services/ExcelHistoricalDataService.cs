
using System.Data;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class ExcelHistoricalDataService : IHistoricalDataRepository
{
    private readonly ILogger<ExcelHistoricalDataService> _logger;
    private readonly string _historicalPath;
    // Cache: TeamName -> List of Matches involved
    private List<HistoricalMatchDto>? _cachedMatches;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ExcelHistoricalDataService(ILogger<ExcelHistoricalDataService> logger)
    {
        _logger = logger;
        _historicalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "historical");
        
        // Register encoding for ExcelDataReader
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private async Task LoadDataAsync()
    {
        // Double-check locking pattern
        if (_cachedMatches != null) return;

        try
        {
            await _lock.WaitAsync();
            if (_cachedMatches != null) return;

            _cachedMatches = new List<HistoricalMatchDto>();
            
            if (!Directory.Exists(_historicalPath))
            {
                _logger.LogWarning("Historical data directory not found at {Path}", _historicalPath);
                return;
            }

            var files = Directory.GetFiles(_historicalPath, "all-euro-data-*.xlsx");
            foreach (var file in files)
            {
                try
                {
                    await using var stream = File.Open(file, FileMode.Open, FileAccess.Read);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                        {
                            UseHeaderRow = true
                        }
                    });

                    foreach (DataTable table in result.Tables)
                    {
                        // Detect column names dynamically
                        var homeCol = GetColumnName(table.Columns, "HomeTeam", "Home", "Team1", "Home Team");
                        var awayCol = GetColumnName(table.Columns, "AwayTeam", "Away", "Team2", "Away Team");
                        
                        if (homeCol == null || awayCol == null) continue;
                        
                        var fthgCol = GetColumnName(table.Columns, "FTHG", "HG", "HomeGoals", "Home Goals");
                        var ftagCol = GetColumnName(table.Columns, "FTAG", "AG", "AwayGoals", "Away Goals");
                        var ftrCol = GetColumnName(table.Columns, "FTR", "Res", "Result", "Full Time Result");
                        var dateCol = GetColumnName(table.Columns, "Date", "MatchDate", "Day");

                        foreach (DataRow row in table.Rows)
                        {
                            try 
                            {
                                var home = row[homeCol]?.ToString();
                                var away = row[awayCol]?.ToString();
                                
                                if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) continue;

                                var fthgStr = fthgCol != null ? row[fthgCol]?.ToString() : "0";
                                var ftagStr = ftagCol != null ? row[ftagCol]?.ToString() : "0";
                                var ftr = ftrCol != null ? row[ftrCol]?.ToString() : "";
                                var dateStr = dateCol != null ? row[dateCol]?.ToString() : "";

                                int.TryParse(fthgStr, out var fthg);
                                int.TryParse(ftagStr, out var ftag);

                                _cachedMatches.Add(new HistoricalMatchDto
                                {
                                    Date = dateStr ?? "",
                                    HomeTeam = home.Trim(),
                                    AwayTeam = away.Trim(),
                                    FTHG = fthg,
                                    FTAG = ftag,
                                    FTR = ftr ?? ""
                                });
                            }
                            catch
                            {
                                // row parse error, skip
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read excel file {File}", file);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private string? GetColumnName(DataColumnCollection columns, params string[] candidates)
    {
        foreach (DataColumn col in columns)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(col.ColumnName, candidate, StringComparison.OrdinalIgnoreCase))
                    return col.ColumnName;
            }
        }
        return null;
    }

    public async Task<List<HistoricalMatchDto>> GetMatchesBetweenTeamsAsync(string teamA, string teamB, int lastN = 20)
    {
        await LoadDataAsync();
        
        if (_cachedMatches == null || _cachedMatches.Count == 0) return [];

        return _cachedMatches
            .Where(m => (IsMatch(m.HomeTeam, teamA) && IsMatch(m.AwayTeam, teamB)) ||
                        (IsMatch(m.HomeTeam, teamB) && IsMatch(m.AwayTeam, teamA)))
            .TakeLast(lastN)
            .Reverse() 
            .ToList();
    }
    
    private bool IsMatch(string s1, string s2)
    {
        if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;

        // 1. Direct equality
        if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Normalization (remove non-alpha)
        var n1 = Normalize(s1);
        var n2 = Normalize(s2);
        if (n1 == n2) return true;
        if (n1.Contains(n2) || n2.Contains(n1)) return true;

        // 3. Known Aliases (Manual overrides for common mismatches)
        if (AreAliases(s1, s2, "Man United", "Manchester United")) return true;
        if (AreAliases(s1, s2, "Man Utd", "Manchester United")) return true;
        if (AreAliases(s1, s2, "Man City", "Manchester City")) return true;
        if (AreAliases(s1, s2, "Wolves", "Wolverhampton Wanderers")) return true;
        if (AreAliases(s1, s2, "Spurs", "Tottenham Hotspur")) return true;
        if (AreAliases(s1, s2, "Nott'm Forest", "Nottingham Forest")) return true;
        if (AreAliases(s1, s2, "Sheff Utd", "Sheffield United")) return true;
        if (AreAliases(s1, s2, "West Ham", "West Ham United")) return true;
        if (AreAliases(s1, s2, "Newcastle", "Newcastle United")) return true;
        if (AreAliases(s1, s2, "Brighton", "Brighton & Hove Albion")) return true;
        if (AreAliases(s1, s2, "Leeds", "Leeds United")) return true;
        if (AreAliases(s1, s2, "Leicester", "Leicester City")) return true;
        if (AreAliases(s1, s2, "Norwich", "Norwich City")) return true;
        
        // European
        if (AreAliases(s1, s2, "Ath Madrid", "Atletico Madrid")) return true;
        if (AreAliases(s1, s2, "Sp Gijon", "Sporting Gijon")) return true;
        if (AreAliases(s1, s2, "Espanol", "Espanyol")) return true;
        if (AreAliases(s1, s2, "Betis", "Real Betis")) return true;
        if (AreAliases(s1, s2, "Celta", "Celta Vigo")) return true;
        if (AreAliases(s1, s2, "Sociedad", "Real Sociedad")) return true;
        if (AreAliases(s1, s2, "Vallecano", "Rayo Vallecano")) return true;
        
        if (AreAliases(s1, s2, "PSG", "Paris Saint Germain")) return true;
        if (AreAliases(s1, s2, "PSG", "Paris SG")) return true;
        if (AreAliases(s1, s2, "St Etienne", "Saint Etienne")) return true;
        
        if (AreAliases(s1, s2, "Inter", "Inter Milan")) return true;
        if (AreAliases(s1, s2, "Milan", "AC Milan")) return true;
        if (AreAliases(s1, s2, "Roma", "AS Roma")) return true;
        
        if (AreAliases(s1, s2, "Gladbach", "B. Monchengladbach")) return true;
        if (AreAliases(s1, s2, "Monchengladbach", "B. Monchengladbach")) return true;
        if (AreAliases(s1, s2, "Hertha", "Hertha Berlin")) return true;
        if (AreAliases(s1, s2, "Mainz", "Mainz 05")) return true;
        if (AreAliases(s1, s2, "Schalke", "Schalke 04")) return true;
        if (AreAliases(s1, s2, "Leverkusen", "Bayer Leverkusen")) return true;
        if (AreAliases(s1, s2, "Frankfurt", "Eintracht Frankfurt")) return true;

        if (AreAliases(s1, s2, "St Truiden", "Sint-Truiden")) return true;
        if (AreAliases(s1, s2, "Waregem", "Zulte Waregem")) return true;
        if (AreAliases(s1, s2, "Standard", "Standard Liege")) return true;

        // 4. Levenshtein Distance
        var dist = ComputeLevenshteinDistance(n1, n2);
        var maxLen = Math.Max(n1.Length, n2.Length);
        
        if (maxLen > 5 && dist <= 2) return true;
        if (maxLen > 10 && dist <= 3) return true;

        return false;
    }

    private string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    private bool AreAliases(string actualA, string actualB, string alias1, string alias2)
    {
        return (string.Equals(actualA, alias1, StringComparison.OrdinalIgnoreCase) && string.Equals(actualB, alias2, StringComparison.OrdinalIgnoreCase)) ||
               (string.Equals(actualA, alias2, StringComparison.OrdinalIgnoreCase) && string.Equals(actualB, alias1, StringComparison.OrdinalIgnoreCase));
    }

    private int ComputeLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
