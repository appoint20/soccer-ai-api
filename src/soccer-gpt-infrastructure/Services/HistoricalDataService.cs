using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

/// <summary>
/// Service for accessing historical match data from Excel files (football-data.co.uk format)
/// </summary>
public class HistoricalDataService : IHistoricalDataService
{
    private readonly ILogger<HistoricalDataService> _logger;
    private readonly string _dataDirectory;
    private readonly Dictionary<int, string> _leagueToDivisionMap;
    private DataSet? _cachedData;
    private bool _isInitialized;

    public HistoricalDataService(ILogger<HistoricalDataService> logger)
    {
        _logger = logger;
        
        // Path to historical Excel files
        _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "historical");
        
        // Map API league IDs to Excel division codes
        _leagueToDivisionMap = new Dictionary<int, string>
        {
            { 39, "E0" },  // Premier League
            { 40, "E1" },  // Championship
            { 41, "E2" },  // League One
            { 42, "E3" },  // League Two
            { 61, "F1" },  // Ligue 1
            { 62, "F2" },  // Ligue 2
            { 78, "D1" },  // Bundesliga 1
            { 79, "D2" },  // Bundesliga 2
            { 135, "I1" }, // Serie A
            { 136, "I2" }, // Serie B
            { 140, "SP1" }, // La Liga
            { 141, "SP2" }  // La Liga 2
        };

        // Required for ExcelDataReader
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc />
    public string GetDivisionCode(int leagueId)
    {
        return _leagueToDivisionMap.TryGetValue(leagueId, out var code) ? code : string.Empty;
    }

    /// <inheritdoc />
    public async Task<HistoricalMatchData?> FindMatchAsync(string homeTeam, string awayTeam, DateTime date, int leagueId)
    {
        await EnsureDataLoadedAsync();
        
        var divisionCode = GetDivisionCode(leagueId);
        if (string.IsNullOrEmpty(divisionCode))
        {
            _logger.LogWarning("Unknown league ID: {LeagueId}", leagueId);
            return null;
        }

        if (_cachedData == null) return null;

        foreach (DataTable table in _cachedData.Tables)
        {
            foreach (DataRow row in table.Rows)
            {
                try
                {
                    var div = GetStringValue(row, "Div");
                    if (div != divisionCode) continue;

                    var matchDate = ParseDate(row, "Date");
                    if (!matchDate.HasValue) continue;

                    // Check if same day
                    if (matchDate.Value.Date != date.Date) continue;

                    var htName = GetStringValue(row, "HomeTeam");
                    var atName = GetStringValue(row, "AwayTeam");

                    // Fuzzy match team names
                    if (FuzzyMatch(htName, homeTeam) && FuzzyMatch(atName, awayTeam))
                    {
                        return CreateMatchData(row, div);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing row in historical data");
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<List<HistoricalMatchData>> GetTeamHistoryAsync(string teamName, int leagueId, DateTime beforeDate, int limit = 6)
    {
        await EnsureDataLoadedAsync();
        
        var divisionCode = GetDivisionCode(leagueId);
        if (string.IsNullOrEmpty(divisionCode) || _cachedData == null)
            return [];

        var matches = new List<HistoricalMatchData>();

        foreach (DataTable table in _cachedData.Tables)
        {
            foreach (DataRow row in table.Rows)
            {
                try
                {
                    var div = GetStringValue(row, "Div");
                    if (div != divisionCode) continue;

                    var matchDate = ParseDate(row, "Date");
                    if (!matchDate.HasValue || matchDate.Value >= beforeDate) continue;

                    var htName = GetStringValue(row, "HomeTeam");
                    var atName = GetStringValue(row, "AwayTeam");

                    // Check if team played (home or away)
                    if (FuzzyMatch(htName, teamName) || FuzzyMatch(atName, teamName))
                    {
                        var matchData = CreateMatchData(row, div);
                        if (matchData != null)
                        {
                            matches.Add(matchData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing row for team history");
                }
            }
        }

        // Sort by date descending and take limit
        return matches
            .OrderByDescending(m => m.Date)
            .Take(limit)
            .ToList();
    }

    private async Task EnsureDataLoadedAsync()
    {
        if (_isInitialized) return;

        await Task.Run(() =>
        {
            _cachedData = new DataSet();
            
            if (!Directory.Exists(_dataDirectory))
            {
                _logger.LogWarning("Historical data directory not found: {Directory}", _dataDirectory);
                _isInitialized = true;
                return;
            }

            var excelFiles = Directory.GetFiles(_dataDirectory, "*.xlsx");
            _logger.LogInformation("Found {Count} Excel files in {Directory}", excelFiles.Length, _dataDirectory);

            foreach (var file in excelFiles)
            {
                try
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    
                    var config = new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration
                        {
                            UseHeaderRow = true
                        }
                    };

                    var result = reader.AsDataSet(config);
                    foreach (DataTable table in result.Tables)
                    {
                        // Rename table to include source file for debugging
                        table.TableName = $"{Path.GetFileNameWithoutExtension(file)}_{table.TableName}";
                        _cachedData.Tables.Add(table.Copy());
                    }

                    _logger.LogInformation("Loaded {File} with {TableCount} sheets", 
                        Path.GetFileName(file), result.Tables.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load Excel file: {File}", file);
                }
            }

            _isInitialized = true;
        });
    }

    private HistoricalMatchData? CreateMatchData(DataRow row, string division)
    {
        var homeTeam = GetStringValue(row, "HomeTeam");
        var awayTeam = GetStringValue(row, "AwayTeam");
        var matchDate = ParseDate(row, "Date");
        var fthg = GetIntValue(row, "FTHG");
        var ftag = GetIntValue(row, "FTAG");

        if (string.IsNullOrEmpty(homeTeam) || string.IsNullOrEmpty(awayTeam) || 
            !matchDate.HasValue || !fthg.HasValue || !ftag.HasValue)
        {
            return null;
        }

        return new HistoricalMatchData
        {
            Date = matchDate.Value,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            Fthg = fthg.Value,
            Ftag = ftag.Value,
            Hthg = GetIntValue(row, "HTHG"),
            Htag = GetIntValue(row, "HTAG"),
            HomeShots = GetIntValue(row, "HS"),
            AwayShots = GetIntValue(row, "AS"),
            HomeShotsOnTarget = GetIntValue(row, "HST"),
            AwayShotsOnTarget = GetIntValue(row, "AST"),
            Division = division,
            // Betting odds from Bet365 columns
            HomeWinOdds = GetDoubleValue(row, "B365H"),
            DrawOdds = GetDoubleValue(row, "B365D"),
            AwayWinOdds = GetDoubleValue(row, "B365A"),
            Over25Odds = GetDoubleValue(row, "B365>2.5"),
            Under25Odds = GetDoubleValue(row, "B365<2.5")
        };
    }

    private static string GetStringValue(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return string.Empty;
        var value = row[column];
        return value == DBNull.Value ? string.Empty : value.ToString() ?? string.Empty;
    }

    private static int? GetIntValue(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return null;
        var value = row[column];
        if (value == DBNull.Value) return null;
        
        if (value is double d) return (int)d;
        if (value is int i) return i;
        if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        
        return null;
    }

    private static double? GetDoubleValue(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return null;
        var value = row[column];
        if (value == DBNull.Value) return null;
        
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is decimal dec) return (double)dec;
        if (double.TryParse(value.ToString(), out var parsed)) return parsed;
        
        return null;
    }

    private static DateTime? ParseDate(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return null;
        var value = row[column];
        if (value == DBNull.Value) return null;
        
        if (value is DateTime dt) return dt;
        
        var dateStr = value.ToString();
        if (string.IsNullOrEmpty(dateStr)) return null;

        // Try common football-data.co.uk date formats
        var formats = new[] { "dd/MM/yyyy", "dd/MM/yy", "yyyy-MM-dd", "MM/dd/yyyy" };
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool FuzzyMatch(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2)) return false;

        // First check explicit alias mappings
        var canonical1 = GetCanonicalName(name1);
        var canonical2 = GetCanonicalName(name2);
        
        if (canonical1 == canonical2) return true;

        // Normalize: lowercase, remove common suffixes
        var n1 = NormalizeName(name1);
        var n2 = NormalizeName(name2);

        // Exact match after normalization
        if (n1 == n2) return true;

        // Contains match (for short names)
        if (n1.Length >= 4 && n2.Length >= 4)
        {
            if (n1.Contains(n2) || n2.Contains(n1)) return true;
        }

        // Check if significant prefix matches (at least 4 chars for better accuracy)
        if (n1.Length >= 4 && n2.Length >= 4)
        {
            var prefix1 = n1.Substring(0, Math.Min(4, n1.Length));
            var prefix2 = n2.Substring(0, Math.Min(4, n2.Length));
            if (prefix1 == prefix2) return true;
        }

        return false;
    }

    /// <summary>
    /// Maps common variations of team names to a canonical form
    /// </summary>
    private static string GetCanonicalName(string name)
    {
        var lower = name.ToLowerInvariant().Trim();
        
        // Comprehensive alias map for English football teams
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Premier League / Championship common variations
            { "man united", "manchester_united" },
            { "man utd", "manchester_united" },
            { "manchester united", "manchester_united" },
            { "manchester utd", "manchester_united" },
            { "man city", "manchester_city" },
            { "manchester city", "manchester_city" },
            { "sheffield utd", "sheffield_united" },
            { "sheffield united", "sheffield_united" },
            { "sheff utd", "sheffield_united" },
            { "sheffield wed", "sheffield_wednesday" },
            { "sheffield wednesday", "sheffield_wednesday" },
            { "sheff wed", "sheffield_wednesday" },
            { "tottenham", "tottenham" },
            { "spurs", "tottenham" },
            { "tottenham hotspur", "tottenham" },
            { "wolves", "wolverhampton" },
            { "wolverhampton", "wolverhampton" },
            { "wolverhampton wanderers", "wolverhampton" },
            { "west ham", "west_ham" },
            { "west ham united", "west_ham" },
            { "newcastle", "newcastle" },
            { "newcastle united", "newcastle" },
            { "newcastle utd", "newcastle" },
            { "nottingham forest", "nottm_forest" },
            { "nott'm forest", "nottm_forest" },
            { "notts forest", "nottm_forest" },
            { "brighton", "brighton" },
            { "brighton & hove albion", "brighton" },
            { "brighton and hove albion", "brighton" },
            { "leicester", "leicester" },
            { "leicester city", "leicester" },
            { "leeds", "leeds" },
            { "leeds united", "leeds" },
            { "west brom", "west_brom" },
            { "west bromwich albion", "west_brom" },
            { "west bromwich", "west_brom" },
            { "stoke", "stoke" },
            { "stoke city", "stoke" },
            { "hull", "hull" },
            { "hull city", "hull" },
            { "ipswich", "ipswich" },
            { "ipswich town", "ipswich" },
            { "norwich", "norwich" },
            { "norwich city", "norwich" },
            { "qpr", "qpr" },
            { "queens park rangers", "qpr" },
            { "millwall", "millwall" },
            { "millwall fc", "millwall" },
            { "birmingham", "birmingham" },
            { "birmingham city", "birmingham" },
            
            // League One / Two teams
            { "mk dons", "mk_dons" },
            { "milton keynes dons", "mk_dons" },
            { "afc wimbledon", "wimbledon" },
            { "wimbledon", "wimbledon" },
            { "notts county", "notts_county" },
            { "notts co", "notts_county" },
            { "stockport", "stockport" },
            { "stockport county", "stockport" },
            { "mansfield", "mansfield" },
            { "mansfield town", "mansfield" },
            { "leyton orient", "leyton_orient" },
            { "orient", "leyton_orient" },
            { "exeter", "exeter" },
            { "exeter city", "exeter" },
            { "bristol city", "bristol_city" },
            { "bristol rovers", "bristol_rovers" },
            { "oxford", "oxford" },
            { "oxford united", "oxford" },
            { "oxford utd", "oxford" },
            { "reading", "reading" },
            { "reading fc", "reading" },
            { "cardiff", "cardiff" },
            { "cardiff city", "cardiff" },
            { "swansea", "swansea" },
            { "swansea city", "swansea" },
            { "preston", "preston" },
            { "preston north end", "preston" },
            { "plymouth", "plymouth" },
            { "plymouth argyle", "plymouth" },
            { "blackburn", "blackburn" },
            { "blackburn rovers", "blackburn" },
            { "bolton", "bolton" },
            { "bolton wanderers", "bolton" },
            { "wigan", "wigan" },
            { "wigan athletic", "wigan" },
            { "charlton", "charlton" },
            { "charlton athletic", "charlton" },
            { "derby", "derby" },
            { "derby county", "derby" },
            { "luton", "luton" },
            { "luton town", "luton" },
            { "huddersfield", "huddersfield" },
            { "huddersfield town", "huddersfield" },
            { "barnsley", "barnsley" },
            { "barnsley fc", "barnsley" },
            { "peterborough", "peterborough" },
            { "peterborough united", "peterborough" },
            { "peterborough utd", "peterborough" },
            { "rotherham", "rotherham" },
            { "rotherham united", "rotherham" },
            { "rotherham utd", "rotherham" },
            { "shrewsbury", "shrewsbury" },
            { "shrewsbury town", "shrewsbury" },
            { "fleetwood", "fleetwood" },
            { "fleetwood town", "fleetwood" },
            { "accrington", "accrington" },
            { "accrington stanley", "accrington" },
            { "accrington st", "accrington" },
            { "doncaster", "doncaster" },
            { "doncaster rovers", "doncaster" },
            { "wycombe", "wycombe" },
            { "wycombe wanderers", "wycombe" },
            { "northampton", "northampton" },
            { "northampton town", "northampton" },
            { "port vale", "port_vale" },
            { "burton", "burton" },
            { "burton albion", "burton" },
            { "lincoln", "lincoln" },
            { "lincoln city", "lincoln" },
            { "swindon", "swindon" },
            { "swindon town", "swindon" },
            { "colchester", "colchester" },
            { "colchester united", "colchester" },
            { "colchester utd", "colchester" },
            { "crawley", "crawley" },
            { "crawley town", "crawley" },
            { "crewe", "crewe" },
            { "crewe alexandra", "crewe" },
            { "harrogate", "harrogate" },
            { "harrogate town", "harrogate" },
            { "gillingham", "gillingham" },
            { "gillingham fc", "gillingham" },
            { "stevenage", "stevenage" },
            { "stevenage fc", "stevenage" },
            { "grimsby", "grimsby" },
            { "grimsby town", "grimsby" },
            { "tranmere", "tranmere" },
            { "tranmere rovers", "tranmere" },
            { "oldham", "oldham" },
            { "oldham athletic", "oldham" },
            { "barnet", "barnet" },
            { "barnet fc", "barnet" },
            { "barrow", "barrow" },
            { "barrow afc", "barrow" },
            { "salford", "salford" },
            { "salford city", "salford" },
            { "cheltenham", "cheltenham" },
            { "cheltenham town", "cheltenham" },
            { "newport", "newport" },
            { "newport county", "newport" },
            { "cambridge", "cambridge" },
            { "cambridge united", "cambridge" },
            { "cambridge utd", "cambridge" },
            { "chesterfield", "chesterfield" },
            { "chesterfield fc", "chesterfield" },
            { "bromley", "bromley" },
            { "bromley fc", "bromley" },
            { "walsall", "walsall" },
            { "walsall fc", "walsall" },
            { "portsmouth", "portsmouth" },
            { "portsmouth fc", "portsmouth" },
            { "pompey", "portsmouth" },
            { "southampton", "southampton" },
            { "southampton fc", "southampton" },
            { "watford", "watford" },
            { "watford fc", "watford" },
            { "middlesbrough", "middlesbrough" },
            { "boro", "middlesbrough" },
            { "wrexham", "wrexham" },
            { "wrexham afc", "wrexham" },
            { "coventry", "coventry" },
            { "coventry city", "coventry" },
            { "blackpool", "blackpool" },
            { "blackpool fc", "blackpool" },
            { "bradford", "bradford" },
            { "bradford city", "bradford" },
        };

        return aliases.TryGetValue(lower, out var canonical) ? canonical : lower;
    }

    private static string NormalizeName(string name)
    {
        var result = name
            .ToLowerInvariant()
            .Replace(" fc", "")
            .Replace(" afc", "")
            .Replace(" city", "")
            .Replace(" town", "")
            .Replace(" rovers", "")
            .Replace(" wanderers", "")
            .Replace(" athletic", "")
            .Replace(" albion", "")
            .Replace(" united", "")
            .Replace(" utd", "")
            .Replace("'", "")
            .Replace("-", " ")
            .Trim();
        
        // Remove extra spaces
        while (result.Contains("  "))
            result = result.Replace("  ", " ");
            
        return result;
    }
    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetAvailableDivisionsAsync()
    {
        await EnsureDataLoadedAsync();
        var stats = new Dictionary<string, int>();
        
        if (_cachedData == null) return stats;

        foreach (DataTable table in _cachedData.Tables)
        {
            if (!table.Columns.Contains("Div")) continue;

            foreach (DataRow row in table.Rows)
            {
                var div = row["Div"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(div))
                {
                    if (!stats.ContainsKey(div)) stats[div] = 0;
                    stats[div]++;
                }
            }
        }
        return stats;
    }

}
