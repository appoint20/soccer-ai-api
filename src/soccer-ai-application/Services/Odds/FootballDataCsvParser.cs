using System.Globalization;

namespace SoccerAi.Application.Services.Odds;

/// <summary>
/// One match row from a football-data.co.uk season file, reduced to what this
/// system can use.
/// </summary>
public sealed record FootballDataRow(
    DateOnly Date,
    string HomeTeam,
    string AwayTeam,
    int? HomeGoals,
    int? AwayGoals,
    double? HomeWin,
    double? Draw,
    double? AwayWin,
    double? Over25,
    double? Under25)
{
    public bool HasAnyPrice =>
        HomeWin is not null || Draw is not null || AwayWin is not null ||
        Over25 is not null || Under25 is not null;
}

/// <summary>
/// Parses football-data.co.uk season CSVs.
///
/// Three properties of these files drive the design:
///
/// 1. <b>Columns move.</b> Bookmakers come and go between seasons, so every
///    column is resolved by header name, never by position.
/// 2. <b>Prices are English-decimal.</b> Parsing "1.85" under a German locale
///    yields 185 — the exact bug that once corrupted this database. Every parse
///    here is culture-invariant and guard-checked.
/// 3. <b>Rows are ragged.</b> Trailing empty fields are common and blank lines
///    end the file; neither is an error.
/// </summary>
public static class FootballDataCsvParser
{
    /// <remarks>
    /// Only Bet365 columns are read, because the product's minimum-price rules
    /// are stated in Bet365 terms. Pre-closing is preferred over closing on
    /// purpose: it is collected days before kickoff, which is when a customer
    /// would actually place the bet. Closing odds are sharper and would make the
    /// backtest look better than the product can be.
    /// </remarks>
    public static IReadOnlyList<FootballDataRow> Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [];

        var header = SplitLine(lines[0].TrimEnd('\r'));
        var index = BuildIndex(header);

        if (!index.ContainsKey("Date") || !index.ContainsKey("HomeTeam")) return [];

        var rows = new List<FootballDataRow>(lines.Length - 1);

        for (var i = 1; i < lines.Length; i++)
        {
            var row = ParseRow(SplitLine(lines[i].TrimEnd('\r')), index);
            if (row is not null) rows.Add(row);
        }

        return rows;
    }

    private static FootballDataRow? ParseRow(string[] fields, IReadOnlyDictionary<string, int> index)
    {
        var date = ParseDate(Field(fields, index, "Date"));
        var home = Field(fields, index, "HomeTeam");
        var away = Field(fields, index, "AwayTeam");

        // A row without a date or teams is padding, not data.
        if (date is null || string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
            return null;

        return new FootballDataRow(
            date.Value,
            home.Trim(),
            away.Trim(),
            ParseInt(Field(fields, index, "FTHG")),
            ParseInt(Field(fields, index, "FTAG")),
            Price(fields, index, "B365H", "B365CH"),
            Price(fields, index, "B365D", "B365CD"),
            Price(fields, index, "B365A", "B365CA"),
            Price(fields, index, "B365>2.5", "B365C>2.5"),
            Price(fields, index, "B365<2.5", "B365C<2.5"));
    }

    /// <summary>Pre-closing price, else the closing price, else nothing.</summary>
    private static double? Price(
        string[] fields, IReadOnlyDictionary<string, int> index, string primary, string closing) =>
        ParsePrice(Field(fields, index, primary)) ?? ParsePrice(Field(fields, index, closing));

    /// <summary>
    /// Culture-invariant, and rejected rather than rescaled when implausible.
    /// A price outside the guard is corrupt, and a corrupt price silently
    /// invalidates every expected-value calculation built on it.
    /// </summary>
    public static double? ParsePrice(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? OddsGuard.Sanitize(value)
            : null;

    /// <summary>Files use dd/mm/yy in older seasons and dd/mm/yyyy in newer ones.</summary>
    public static DateOnly? ParseDate(string? raw)
    {
        string[] formats = ["dd/MM/yyyy", "dd/MM/yy", "d/M/yyyy", "d/M/yy"];

        return DateOnly.TryParseExact(
            raw?.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Field(string[] fields, IReadOnlyDictionary<string, int> index, string column) =>
        index.TryGetValue(column, out var position) && position < fields.Length
            ? fields[position]
            : null;

    private static Dictionary<string, int> BuildIndex(string[] header)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
        {
            // Files are UTF-8 with a byte-order mark, which would otherwise
            // become part of the first column's name.
            var name = header[i].TrimStart('﻿').Trim();
            // Duplicated headers occur; the first occurrence wins.
            if (name.Length > 0) index.TryAdd(name, i);
        }

        return index;
    }

    /// <summary>
    /// Minimal CSV split. Team names may contain commas inside quotes, so quoted
    /// sections are respected; nothing else in these files needs escaping.
    /// </summary>
    private static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inQuotes:
                    fields.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
