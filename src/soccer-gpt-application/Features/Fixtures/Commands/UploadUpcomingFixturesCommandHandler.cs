using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Extensions;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Features.Fixtures.Commands;

public sealed class UploadUpcomingFixturesCommandHandler(
    IApplicationDbContext dbContext,
    ILogger<UploadUpcomingFixturesCommandHandler> logger)
    : ICommandHandler<UploadUpcomingFixturesCommand, UploadUpcomingFixturesResponse>
{
    private static readonly string[] SupportedLeagues =
    [
        "E0", "E1", "E2", "E3",
        "D1",
        "I1", "I2",
        "F1", "F2",
        "SP1",
    ];
    public async Task<UploadUpcomingFixturesResponse> Handle(
        IReceiveContext<UploadUpcomingFixturesCommand> context,
        CancellationToken cancellationToken)
    {
        var response = new UploadUpcomingFixturesResponse();

        var existingSignatures = await LoadExistingSignatures(cancellationToken);

        using var reader = CreateCsvReader(context.Message.FileStream);
        var dataSet = reader.AsDataSet(CreateExcelConfig());

        if (dataSet.Tables.Count == 0)
        {
            response.Errors.Add("Empty CSV file");
            return response;
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var fixtures = ParseFixtures(
                dataSet.Tables[0],
                existingSignatures,
                response);

            if (fixtures.Count > 0)
            {
                dbContext.Fixtures.AddRange(fixtures);
                await dbContext.SaveChangesAsync(cancellationToken);
                response.AddedCount = fixtures.Count;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Uploading upcoming fixtures failed");

            response.Errors.Add("Upload failed: " + ex.Message);
        }

        return response;
    }

    // -------------------- Parsing --------------------

    private static List<Fixture> ParseFixtures(
        DataTable table, HashSet<string> knownSignatures, UploadUpcomingFixturesResponse response)
    {
        var fixtures = new List<Fixture>();

        foreach (DataRow row in table.Rows)
        {
            response.ProcessedCount++;

            var raw = ExtractRawRow(table, row);
            if (raw == null)
            {
                response.SkippedInvalid++;
                continue;
            }

            if (!TryCreateSignature(raw, out var signature))
            {
                response.SkippedInvalid++;
                continue;
            }

            if (knownSignatures.Contains(signature))
            {
                response.SkippedDuplicate++;
                continue;
            }

            fixtures.Add(CreateFixture(raw, signature));
            knownSignatures.Add(signature);
            response.ValidCount++;
        }

        return fixtures;
    }

    // -------------------- Row Extraction --------------------

    private static RawFixtureRow? ExtractRawRow(DataTable table, DataRow row)
    {
        if (!SupportedLeagues.Contains(Get("Div")))
            return null;
        
        var div = Get("Div");
        var date = Get("Date");
        var home = Get("HomeTeam");
        var away = Get("AwayTeam");

        if (string.IsNullOrWhiteSpace(div) ||
            string.IsNullOrWhiteSpace(date) ||
            string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away))
            return null;

        if (home == away)
            return null;

        if (!TryParseDate(date, out var parsedDate))
            return null;

        return new RawFixtureRow(
            League: div.GetLeagueNameBy(),
            Date: parsedDate,
            Time: ParseTime(Get("Time")),
            Home: home,
            Away: away,
            Row: row,
            Table: table);

        string? Get(string col) =>
            table.Columns.Contains(col)
                ? row[col].ToString()?.Trim()
                : null;
    }

    // -------------------- Signature --------------------

    private static bool TryCreateSignature(
        RawFixtureRow raw,
        out string signature)
    {
        signature = CreateSignature(
            raw.Date,
            raw.Time,
            raw.League,
            raw.Home,
            raw.Away);

        return true;
    }

    // -------------------- Fixture Creation --------------------

    private static Fixture CreateFixture(
        RawFixtureRow raw,
        string signature)
    {
        double? ParseOdd(string col)
        {
            if (!raw.Table.Columns.Contains(col)) return null;

            var val = raw.Row[col]?.ToString();
            return double.TryParse(
                       val,
                       NumberStyles.Any,
                       CultureInfo.InvariantCulture,
                       out var d) && d > 0
                ? d
                : null;
        }

        return new Fixture
        {
            Date = raw.Date,
            Time = raw.Time,
            LeagueName = raw.League,
            HomeName = raw.Home,
            AwayName = raw.Away,
            Signature = signature,

            HomeOdds = ParseOdd("HomeOdds") ?? ParseOdd("1"),
            DrawOdds = ParseOdd("DrawOdds") ?? ParseOdd("X"),
            AwayOdds = ParseOdd("AwayOdds") ?? ParseOdd("2"),
            Over25Odds = ParseOdd("Over25Odds") ?? ParseOdd("O2.5"),
            Under25Odds = ParseOdd("Under25Odds") ?? ParseOdd("U2.5"),
            BttsOdds = ParseOdd("BttsOdds") ?? ParseOdd("BTTS_Yes"),
            TwoToThreeGoalsOdds = ParseOdd("TwoToThreeGoalsOdds"),

            Played = false
        };
    }

    // -------------------- Infrastructure --------------------

    private async Task<HashSet<string>> LoadExistingSignatures(CancellationToken ct)
    {
        var signatures = await dbContext.Fixtures
            .AsNoTracking()
            .Select(f => f.Signature)
            .ToListAsync(ct);

        return new HashSet<string>(signatures);
    }

    private static IExcelDataReader CreateCsvReader(Stream stream) =>
        ExcelReaderFactory.CreateCsvReader(
            stream,
            new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.GetEncoding(1252)
            });

    private static ExcelDataSetConfiguration CreateExcelConfig() =>
        new()
        {
            ConfigureDataTable = _ =>
                new ExcelDataTableConfiguration { UseHeaderRow = true }
        };

    // -------------------- Helpers --------------------

    private static bool TryParseDate(string input, out DateTime date)
    {
        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd/MM/yyyy",
            "MM/dd/yyyy"
        };

        return DateTime.TryParseExact(
                   input,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out date)
               || DateTime.TryParse(input, out date);
    }

    private static TimeSpan ParseTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return TimeSpan.Zero;

        if (TimeSpan.TryParse(input, out var t))
            return t;

        if (DateTime.TryParse(input, out var dt))
            return dt.TimeOfDay;

        return TimeSpan.Zero;
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

    // -------------------- Records --------------------

    private sealed record RawFixtureRow(
        string League,
        DateTime Date,
        TimeSpan Time,
        string Home,
        string Away,
        DataRow Row,
        DataTable Table);
}
