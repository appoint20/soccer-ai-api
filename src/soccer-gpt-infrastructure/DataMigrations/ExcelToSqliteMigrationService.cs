using System.Data;
using System.Text;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_infrastructure.Persistence;

namespace soccer_gpt_infrastructure.DataMigrations;

public partial class ExcelToSqliteMigrationService(
    IServiceProvider serviceProvider, // Use ServiceProvider to create new scope for DbContext
    ILogger<ExcelToSqliteMigrationService> logger)
    : IExcelToSqliteMigrationService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<ExcelToSqliteMigrationService> _logger = logger;
    private readonly string _historicalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "historical");
    private readonly string[] _supportedLeagues = ["E0", "E1", "E2", "E3", "D1", "I1", "I2", "F1", "F2", "SP1"];
    
    // 7. Migration Locking (In-Process)
    private static readonly SemaphoreSlim _migrationLock = new(1, 1);

    static ExcelToSqliteMigrationService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!await _migrationLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Migration is already in progress. Skipping request.");
            return new MigrationResult { Errors = ["Migration already in progress"] };
        }

        try
        {
            _logger.LogInformation("Starting Full Excel to SQLite Migration...");
            
            // Scope creation needed? Constructor injected DbContext might be stale if singleton? 
            // Service is registered as Scoped usually. If Controller calls it, it has a scope.
            // But for heavy batch ops, clean context is good.
            // However, method injection is cleaner. I'll rely on the caller's scope via constructor injection 
            // BUT wait, I changed constructor to `IServiceProvider serviceProvider` to manage my own context/transactions/optimizations if needed?
            // "Disable EF change tracking during import" -> easy on context.
            // Let's assume standard Scoped service usage. I will modify constructor to inject DbContext again but ensure we manage it correctly.
            // Wait, previous file used `ApplicationDbContext`. I replaced it with `IServiceProvider` in the signature? 
            // No, I'll switch back to direct DbContext injection but use `ChangeTracker` settings.
            
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);

            if (!Directory.Exists(_historicalPath))
            {
                return new MigrationResult { Errors = [$"Path not found: {_historicalPath}"] };
            }

            var files = Directory.GetFiles(_historicalPath, "*.xlsx");
            var combinedResult = new MigrationResult();

            // Load Global State (Teams)
            var teamsMap = await LoadTeamsMapAsync(dbContext, cancellationToken);
            var matchSignatures = await LoadMatchSignaturesAsync(dbContext, cancellationToken);

            foreach (var file in files)
            {
                var isCurrentSeason = IsCurrentSeasonFile(file);
                var fileName = Path.GetFileName(file);
                _logger.LogInformation("Processing {File}...", fileName);

                await using var stream = File.Open(file, FileMode.Open, FileAccess.Read);
                var fileResult = await ProcessStreamInternalAsync(dbContext, stream, fileName, isCurrentSeason, teamsMap, matchSignatures, cancellationToken);
                
                // Aggregate Results
                combinedResult.MatchesProcessed += fileResult.MatchesProcessed;
                combinedResult.MatchesAdded += fileResult.MatchesAdded;
                combinedResult.MatchesSkipped += fileResult.MatchesSkipped;
                combinedResult.Errors.AddRange(fileResult.Errors);
            }
            
            _logger.LogInformation("Full Migration Complete. Added: {Added}, Skipped: {Skipped}", combinedResult.MatchesAdded, combinedResult.MatchesSkipped);
            return combinedResult;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    public async Task<MigrationResult> MigrateStreamAsync(Stream stream, string filename, CancellationToken cancellationToken = default)
    {
        if (!await _migrationLock.WaitAsync(0, cancellationToken))
        {
             return new MigrationResult { Errors = ["Migration already in progress"] };
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
             await dbContext.Database.EnsureCreatedAsync(cancellationToken);

            var teamsMap = await LoadTeamsMapAsync(dbContext, cancellationToken);
            var matchSignatures = await LoadMatchSignaturesAsync(dbContext, cancellationToken);
            var isCurrentSeason = IsCurrentSeasonFile(filename);

            return await ProcessStreamInternalAsync(dbContext, stream, filename, isCurrentSeason, teamsMap, matchSignatures, cancellationToken);
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    private async Task<Dictionary<string, Team>> LoadTeamsMapAsync(ApplicationDbContext dbContext, CancellationToken ct)
    {
        return await dbContext.Teams
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, ct);
    }

    private async Task<HashSet<(DateTime, int, int)>> LoadMatchSignaturesAsync(ApplicationDbContext dbContext, CancellationToken ct)
    {
        // Only loading basic signature to keep memory usage low
        var matches = await dbContext.Matches
            .AsNoTracking()
            .Select(m => new { m.Date, m.HomeTeamId, m.AwayTeamId })
            .ToListAsync(ct);
            
        return new HashSet<(DateTime, int, int)>(
            matches.Select(m => (m.Date, m.HomeTeamId, m.AwayTeamId)));
    }

    private async Task<MigrationResult> ProcessStreamInternalAsync(
        ApplicationDbContext dbContext,
        Stream stream, 
        string filename, 
        bool isCurrentSeason,
        Dictionary<string, Team> teamsMap,
        HashSet<(DateTime, int, int)> matchSignatures,
        CancellationToken cancellationToken)
    {
        var result = new MigrationResult();
        
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        });

        // 4. Batch Settings
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        foreach (DataTable table in dataSet.Tables)
        {
            var cols = new ColumnMappings(table.Columns);
            if (!cols.IsValid) continue;

            // 5. Team Creation Optimization (Batch)
            var newTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowCache = new List<MatchRawData>();

            foreach (DataRow row in table.Rows)
            {
                // Basic validations and Data Extraction
                var div = row[cols.Div]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(div) || !_supportedLeagues.Contains(div)) continue; // Not invalid, just skipped
                
                var hName = row[cols.HomeTeam]?.ToString()?.Trim();
                var aName = row[cols.AwayTeam]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(hName) || string.IsNullOrEmpty(aName)) continue;

                // Capture potential new teams
                if (!teamsMap.ContainsKey(hName)) newTeams.Add(hName);
                if (!teamsMap.ContainsKey(aName)) newTeams.Add(aName);
                
                // Parse Date Robustly
                if (!TryRobustParseDate(row[cols.Date], out var date)) continue;

                // Cache for processing
                rowCache.Add(new MatchRawData(row, cols, hName, aName, date, div));
            }

            // Insert New Teams
            if (newTeams.Count > 0)
            {
                var teamsToAdd = newTeams.Select(n => new Team { Name = n }).ToList();
                dbContext.Teams.AddRange(teamsToAdd);
                await dbContext.SaveChangesAsync(cancellationToken);
                
                foreach (var t in teamsToAdd) teamsMap[t.Name] = t; // Update cache
            }

            // 4. Batch Inserts
            int batchCount = 0;
            var batchSize = 500;

            foreach (var item in rowCache)
            {
                result.MatchesProcessed++;

                // Teams MUST exist now
                var hTeam = teamsMap[item.HomeTeam];
                var aTeam = teamsMap[item.AwayTeam];

                // 3. Signature Check
                if (matchSignatures.Contains((item.Date, hTeam.Id, aTeam.Id)))
                {
                    result.MatchesSkipped++;
                    continue;
                }

                // Time Parsing
                var time = TimeSpan.Zero;
                 if (item.Cols.Time != null && item.Row[item.Cols.Time] is object tObj)
                 {
                      TryRobustParseTime(tObj, out time);
                 }

                // Match Creation
                var match = new Match
                {
                    Date = item.Date,
                    Time = time,
                    LeagueName = item.Div,
                    HomeTeamId = hTeam.Id,
                    AwayTeamId = aTeam.Id,
                    FullTimeHomeGoal = ParseInt(item.Row, item.Cols.FtHg),
                    FullTimeAwayGoal = ParseInt(item.Row, item.Cols.FtAg),
                    FullTimeResult = item.Row[item.Cols.FTR]?.ToString() ?? "",
                    HalfTimeHomeGoal = ParseInt(item.Row, item.Cols.htHg),
                    HalfTimeAwayGoal = ParseInt(item.Row, item.Cols.HtAg),
                    HalfTimeResult = item.Row[item.Cols.HTR]?.ToString() ?? "",
                    Referee = item.Cols.Referee != null ? (item.Row[item.Cols.Referee]?.ToString() ?? "") : "",
                    CurrentSeason = isCurrentSeason
                };

                dbContext.Matches.Add(match);
                matchSignatures.Add((item.Date, hTeam.Id, aTeam.Id));
                result.MatchesAdded++;
                batchCount++;

                if (batchCount >= batchSize)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear(); // Clear identity map
                    batchCount = 0;
                }
            }
            
            // Final Save
            if (batchCount > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
        }

        // Restore Tracking
        dbContext.ChangeTracker.AutoDetectChangesEnabled = true;
        dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

        return result;
    }

    // 6. Excel Parsing Robustness
    private bool TryRobustParseDate(object? cellValue, out DateTime date)
    {
        date = default;
        if (cellValue == null) return false;

        // OADate (double)
        if (cellValue is double dVal)
        {
            try { date = DateTime.FromOADate(dVal); return true; } catch {}
        }
        
        // String detection
        var str = cellValue.ToString();
        return DateTime.TryParse(str, out date);
    }
    
    private void TryRobustParseTime(object cellValue, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (cellValue == null) return;
        
        if (cellValue is double dVal)
        {
             // 0.5 = 12:00 PM
             try { time = TimeSpan.FromDays(dVal); return; } catch {}
        }
        
        if (cellValue is DateTime dt)
        {
            time = dt.TimeOfDay;
            return;
        }

        TimeSpan.TryParse(cellValue.ToString(), out time);
    }

    private int ParseInt(DataRow row, string? col)
    {
        if (col == null || row[col] == null) return 0;
        int.TryParse(row[col].ToString(), out var val);
        return val;
    }
    
    private static bool IsCurrentSeasonFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var match = MyRegex().Match(fileName);
        
        if (!match.Success) return false;
        
        if (!int.TryParse(match.Groups[1].Value, out var y1) || !int.TryParse(match.Groups[2].Value, out var y2)) 
            return false;
        
        var now = DateTime.Now;
        return now.Year == y1 || now.Year == y2; 
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(\d{4})-(\d{4})")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    private record MatchRawData(DataRow Row, ColumnMappings Cols, string HomeTeam, string AwayTeam, DateTime Date, string Div);

    private class ColumnMappings
    {
        public string? Div { get; }
        public string? HomeTeam { get; }
        public string? AwayTeam { get; }
        public string? Date { get; }
        public string? Time { get; }
        public string? FtHg { get; }
        public string? FtAg { get; }
        public string? FTR { get; }
        public string? htHg { get; }
        public string? HtAg { get; }
        public string? HTR { get; }
        public string? Referee { get; }

        public bool IsValid => Div != null && HomeTeam != null && AwayTeam != null && Date != null;

        public ColumnMappings(DataColumnCollection cols)
        {
            Div = Get(cols, "Div");
            HomeTeam = Get(cols, "HomeTeam");
            AwayTeam = Get(cols, "AwayTeam");
            Date = Get(cols, "Date");
            Time = Get(cols, "Time");
            FtHg = Get(cols, "FTHG", "HG");
            FtAg = Get(cols, "FTAG", "AG");
            FTR = Get(cols, "FTR", "Res");
            htHg = Get(cols, "HTHG");
            HtAg = Get(cols, "HTAG");
            HTR = Get(cols, "HTR");
            Referee = Get(cols, "Referee", "Ref");
        }

        private static string? Get(DataColumnCollection columns, params string[] candidates)
            => columns.Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .FirstOrDefault(name => candidates.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
