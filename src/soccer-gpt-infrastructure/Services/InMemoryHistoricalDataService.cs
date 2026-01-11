using System.Data;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Models;
using System.Collections.Concurrent;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class InMemoryHistoricalDataService : IHistoricalDataRepository, IHostedService
{
    private readonly string _historicalPath;
    private readonly ILeaguesRepository _leaguesRepository;
    private readonly ILogger<InMemoryHistoricalDataService> _logger;
    
    private bool _isLoaded;
    private List<HistoricalMatchDto> _allMatches = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<HistoricalMatchDto>> _teamMatches 
        = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryHistoricalDataService(
        ILogger<InMemoryHistoricalDataService> logger,
        ILeaguesRepository leaguesRepository)
    {
        _logger = logger;
        _leaguesRepository = leaguesRepository;
        _historicalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "historical");
        
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded) return;
        
        await _initLock.WaitAsync(cancellationToken);
        try 
        {
            if (_isLoaded) return;

            _logger.LogInformation("Starting In-Memory Historical Data Loading...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var leagues = await _leaguesRepository.GetLeaguesAsync(cancellationToken);
            var supportedLeagues = leagues
                .Select(l => l.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(_historicalPath))
            {
                _logger.LogWarning("Historical data path not found: {Path}", _historicalPath);
                return;
            }

            var files = Directory.GetFiles(_historicalPath, "all-euro-data-*.xlsx");
            
            foreach (var file in files)
            {
                await LoadFileAsync(file, supportedLeagues, cancellationToken);
            }
            
            // Finalize lists (sort by date)
            foreach (var key in _teamMatches.Keys)
            {
                _teamMatches[key] = _teamMatches[key]
                    .OrderBy(m => m.Date)
                    .ToList();
            }
            _allMatches = _allMatches
                .OrderBy(m => m.Date)
                .ToList();

            _isLoaded = true;
            stopwatch.Stop();
            _logger.LogInformation("LODADED {Count} historical matches in {Elapsed}ms", _allMatches.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to initialize historical data");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LoadFileAsync(
        string filePath, HashSet<string> supportedLeagues, CancellationToken cancellationToken)
    {
        await Task.Run(() => 
        {
            try 
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                });

                foreach (DataTable table in result.Tables)
                {
                    // Required Columns
                    var divCol = GetColumnName(table.Columns, "Div");
                    var homeCol = GetColumnName(table.Columns, "HomeTeam");
                    var awayCol = GetColumnName(table.Columns, "AwayTeam");
                    var dateCol = GetColumnName(table.Columns, "Date");
                    var timeCol = GetColumnName(table.Columns, "Time");
                    
                    if (divCol == null || homeCol == null || awayCol == null || dateCol == null) continue;

                    // Data Columns
                    var fthgCol = GetColumnName(table.Columns, "FTHG");
                    var ftagCol = GetColumnName(table.Columns, "FTAG");
                    var ftrCol = GetColumnName(table.Columns, "FTR");


                    foreach (DataRow row in table.Rows)
                    {
                        var div = row[divCol]?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(div)) continue;

                        // Filter by League
                        if (!supportedLeagues.Contains(div)) continue;

                        var home = row[homeCol]?.ToString()?.Trim();
                        var away = row[awayCol]?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) continue;

                        var dateStr = row[dateCol]?.ToString();
                        if (!DateTime.TryParse(dateStr, out var date)) continue;

                        // Parse Stats
                        int.TryParse(row[fthgCol]?.ToString(), out var fthg);
                        int.TryParse(row[ftagCol]?.ToString(), out var ftag);
                        var ftr = row[ftrCol]?.ToString() ?? "";
                        var time = timeCol != null ? (row[timeCol]?.ToString() ?? "") : "";

                        var match = new HistoricalMatchDto
                        {
                            Date = date,
                            Time = time,
                            Div = div,
                            HomeTeam = home,
                            AwayTeam = away,
                            FTHG = fthg,
                            FTAG = ftag,
                            FTR = ftr
                        };
                        
                        _allMatches.Add(match);

                        // Add to lookup for Home Team
                        _teamMatches.AddOrUpdate(home, [match], (_, list) => { list.Add(match); return list; });
                        
                        // Add to lookup for Away Team
                        _teamMatches.AddOrUpdate(away, [match], (_, list) => { list.Add(match); return list; });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file {File}", filePath);
            }
        }, cancellationToken);
    }
    
    // IHistoricalDataRepository implementation

    public async Task<List<HistoricalMatchDto>> GetMatchesBetweenTeamsAsync(string teamA, string teamB, int lastN = 20)
    {
        // Wait for init if needed (though HostedService should have run)
        if (!_isLoaded) await InitializeAsync();

        var matchesA = GetMatchesForTeam(teamA);
        
        var headToHead = matchesA
            .Where(m => MatchTeam(m, teamB))
            .OrderByDescending(m => m.Date) // Newest first
            .Take(lastN)
            .Reverse() // Return Chronological? Original was reverse take lastN. 
                       // Wait, original: TakeLast(N).Reverse() -> Newest First?
                       // Let's stick to standard behavior: Get matches, sort descending dates, take N.
            .ToList();
            
        return headToHead;
    }

    public async Task<List<HistoricalMatchDto>> GetAllMatchesAsync()
    {
        if (!_isLoaded) await InitializeAsync();
        return _allMatches;
    }

    public List<HistoricalMatchDto> GetMatchesForTeam(string teamName)
    {
        if (_teamMatches.TryGetValue(teamName, out var matches))
        {
            return matches;
        }
        
        // Alias/Normalization fallback
        var normalized = Normalize(teamName);
        var key = _teamMatches.Keys.FirstOrDefault(k => Normalize(k) == normalized);
        
        return key != null ? _teamMatches[key] : new List<HistoricalMatchDto>();
    }

    private static bool MatchTeam(HistoricalMatchDto m, string targetTeam)
    {
        return string.Equals(m.HomeTeam, targetTeam, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(m.AwayTeam, targetTeam, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetColumnName(DataColumnCollection columns, string candidate)
    {
        return (
            from DataColumn col in columns 
            where string.Equals(col.ColumnName, candidate, StringComparison.OrdinalIgnoreCase) 
            select col.ColumnName
        ).FirstOrDefault();
    }
    
    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Where(char.IsLetterOrDigit))
            sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
