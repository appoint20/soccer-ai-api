using System.Data;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Extensions;

public static partial class MatchExtensions
{
    private static readonly Dictionary<string, string> LeagueMappings = new()
    {
        { "E0", "Premier League" },
        { "E1", "Championship" },
        { "E2", "League One" },
        { "E3", "League Two" },
        { "D1", "Bundesliga" },
        { "I1", "Serie A" },
        { "I2", "Serie B" },
        { "SP1", "La Liga" },
        { "F1", "Ligue 1" },
        { "F2", "Ligue 2" }
    };
    
    public static string? GetLeagueNameBy(this string leagueCode) 
        => LeagueMappings.GetValueOrDefault(leagueCode);
    
    public static bool IsCurrentSeasonFile(this string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var match = MyRegex().Match(fileName);
        
        if (!match.Success) return false;
        
        if (!int.TryParse(match.Groups[1].Value, out var y1) ||
            !int.TryParse(match.Groups[2].Value, out var y2)) 
            return false;
        
        var now = DateTime.Now;
        return now.Year == y1 || now.Year == y2; 
    }
    
    private static string CreateSignature(
        DateTime date,
        TimeSpan time,
        string league,
        string home,
        string away)
    {
        return $"{date:yyyyMMdd}_{time:hhmm}_{league}_{home}_{away}"
            .ToUpperInvariant();
    }

    public static int ParseInt(this DataRow row, string? column)
    {
        if (string.IsNullOrEmpty(column) || !row.Table.Columns.Contains(column)) return 0;
        return int.TryParse(row[column]?.ToString(), out var v) ? v : 0;
    }
    
    public static bool TryRobustParseDate(this object? cellValue, out DateTime date)
    {
        date = default;
        switch (cellValue)
        {
            case null:
                return false;
            case double dVal:
                try { date = DateTime.FromOADate(dVal); return true; }
                catch { /* ignored */ }

                break;
        }

        // String detection
        var str = cellValue.ToString();
        return DateTime.TryParse(str, out date);
    }
    
    public static TimeSpan TryRobustParseTime(this object cellValue)
    {
        if (cellValue == null || cellValue == DBNull.Value)
            throw new Exception("Error occurs for time parsing");

        var strVal = cellValue.ToString()?.Trim();
        
        if (string.IsNullOrEmpty(strVal)) 
            throw new Exception("Error occurs for time parsing");

        if (double.TryParse(strVal, out var dVal))
        {
            try
            {
                return dVal < 1.0 
                    ? TimeSpan.FromDays(dVal) 
                    : DateTime.FromOADate(dVal).TimeOfDay;
            } 
            catch { /*ignore*/ }
        }
        
        if (cellValue is DateTime dt)
        {
            return dt.TimeOfDay;
        }

        // 3. String Parsing (HH:mm:ss, HH:mm, etc)
        if (TimeSpan.TryParse(strVal, out var time)) return time;
        
        // Try ParseExact for common formats if simple parse fails
        string[] formats = ["HH:mm", "HH:mm:ss", "h:mm tt", "H:mm"];
        return DateTime.TryParseExact(
            strVal, 
            formats, 
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsedDt) 
            ? parsedDt.TimeOfDay 
            : DateTime.Now.TimeOfDay;
    }

    
    [System.Text.RegularExpressions.GeneratedRegex(@"(\d{4})-(\d{4})")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
    
}