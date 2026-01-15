using System.Data;
using ExcelDataReader;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Extensions;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.HistoricalMatches.Commands;

public sealed class UploadHistoricalDataCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UploadHistoricalDataCommand, UploadHistoricalDataResponse>
{
    private static readonly string[] SupportedLeagues =
    [
        "E0", "E1", "E2", "E3",
        "D1",
        "I1", "I2",
        "F1", "F2",
        "SP1"
    ];

    public async Task<UploadHistoricalDataResponse> Handle(
        IReceiveContext<UploadHistoricalDataCommand> context,
        CancellationToken cancellationToken)
    {
        var command = context.Message;
        var isCurrentSeason = command.FileName.IsCurrentSeasonFile();

        var response = new UploadHistoricalDataResponse();

        var teamsMap = await LoadTeamsAsync(cancellationToken);
        var matchSignatures = await LoadMatchSignaturesAsync(cancellationToken);

        using var reader = ExcelReaderFactory.CreateReader(command.FileStream);
        var dataSet = reader.AsDataSet(CreateExcelConfig());

        foreach (DataTable table in dataSet.Tables)
        {
            ProcessTable(
                table,
                response,
                teamsMap,
                matchSignatures,
                isCurrentSeason,
                cancellationToken);
        }

        return response;
    }

    // -------------------- Table Processing --------------------

    private void ProcessTable(
        DataTable table,
        UploadHistoricalDataResponse response,
        Dictionary<string, Team> teamsMap,
        HashSet<MatchSignature> signatures,
        bool isCurrentSeason,
        CancellationToken cancellationToken)
    {
        var mappings = new ColumnMappings(table.Columns);
        if (!mappings.IsValid) return;

        var parsedMatches = ParseMatches(table, mappings, teamsMap);

        InsertNewTeams(parsedMatches.NewTeams, teamsMap, cancellationToken);

        ProcessMatchesBatch(
            parsedMatches.Matches,
            response,
            teamsMap,
            signatures,
            isCurrentSeason,
            cancellationToken);
    }
    
    private static ParsedMatchesResult ParseMatches(DataTable table, ColumnMappings cols, Dictionary<string, Team> teamsMap)
    {
        var newTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<MatchRawData>();

        foreach (DataRow row in table.Rows)
        {
            var leagueCode = row[cols.Div].ToString()?.Trim();
            if (!SupportedLeagues.Contains(leagueCode)) continue;

            var leagueName = leagueCode!.GetLeagueNameBy();

            var home = row[cols.HomeTeam].ToString()?.Trim();
            var away = row[cols.AwayTeam].ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(home) ||
                string.IsNullOrWhiteSpace(away) ||
                home.Equals(away, StringComparison.OrdinalIgnoreCase) == true)
                continue;

            if (!row[cols.Date].TryRobustParseDate(out var date)) 
                continue;

            var time = row[cols.Time].TryRobustParseTime();

            if (!teamsMap.ContainsKey(home)) newTeams.Add(home);
            if (!teamsMap.ContainsKey(away)) newTeams.Add(away);

            matches.Add(new MatchRawData(
                row,
                cols,
                home,
                away,
                date,
                time,
                leagueName));
        }

        return new ParsedMatchesResult(matches, newTeams);
    }
    
    private async Task InsertNewTeams(HashSet<string> newTeams, Dictionary<string, Team> teamsMap, CancellationToken ct)
    {
        if (newTeams.Count == 0) return;

        var entities = newTeams
            .Select(n => new Team { Name = n })
            .ToList();

        await dbContext.Teams.AddRangeAsync(entities, ct);
        await dbContext.SaveChangesAsync(ct);

        foreach (var team in entities)
        {
            teamsMap[team.Name] = team;
        }
    }

    private async Task<Dictionary<string, Team>> LoadTeamsAsync(CancellationToken ct)
    {
        return await dbContext.Teams
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, ct);
    }
    
    private async Task<HashSet<MatchSignature>> LoadMatchSignaturesAsync(CancellationToken ct)
    {
        // Only loading basic signature to keep memory usage low
        var matches = await dbContext.Matches
            .AsNoTracking()
            .Select(m => new MatchSignature(m.Date, m.HomeTeamId, m.AwayTeamId))
            .ToListAsync(ct);
            
        return
        [
            ..matches
        ];
    }

    private async Task ProcessMatchesBatch(
        List<MatchRawData> matches, 
        UploadHistoricalDataResponse response, Dictionary<string, Team> teamsMap, 
        HashSet<MatchSignature> signatures, bool isCurrentSeason, CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var batchCounter = 0;

        foreach (var raw in matches)
        {
            response.ProcessedCount++;

            var homeTeam = teamsMap[raw.HomeTeam];
            var awayTeam = teamsMap[raw.AwayTeam];

            var signature = new MatchSignature(
                raw.Date,
                homeTeam.Id,
                awayTeam.Id);

            if (signatures.Contains(signature))
            {
                response.SkippedDuplicate++;
                continue;
            }

            var match = CreateMatch(raw, homeTeam.Id, awayTeam.Id, isCurrentSeason);

            dbContext.Matches.Add(match);
            signatures.Add(signature);

            response.AddedCount++;
            batchCounter++;

            if (batchCounter < batchSize) 
                continue;
            
            await dbContext.SaveChangesAsync(cancellationToken);
            batchCounter = 0;
        }

        if (batchCounter > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Match CreateMatch(MatchRawData raw, int homeTeamId, int awayTeamId, bool isCurrentSeason)
    {
        return new Match
        {
            Date = raw.Date,
            Time = raw.Time,
            LeagueName = raw.LeagueName,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,

            FullTimeHomeGoal = raw.Row.ParseInt(raw.Cols.FtHg),
            FullTimeAwayGoal = raw.Row.ParseInt( raw.Cols.FtAg),
            FullTimeResult = raw.Row[raw.Cols.FTR]?.ToString() ?? "",

            HalfTimeHomeGoal = raw.Row.ParseInt(raw.Cols.htHg),
            HalfTimeAwayGoal = raw.Row.ParseInt(raw.Cols.HtAg),
            HalfTimeResult = raw.Row[raw.Cols.HTR]?.ToString() ?? "",

            Referee = raw.Cols.Referee != null
                ? raw.Row[raw.Cols.Referee]?.ToString() ?? ""
                : "",

            CurrentSeason = isCurrentSeason
        };
    }
    

    private static ExcelDataSetConfiguration CreateExcelConfig() =>
        new()
        {
            ConfigureDataTable = _ =>
                new ExcelDataTableConfiguration { UseHeaderRow = true }
        };

    // -------------------- Records --------------------

    private sealed record ParsedMatchesResult(
        List<MatchRawData> Matches,
        HashSet<string> NewTeams);

    private sealed record MatchSignature(
        DateTime Date,
        int HomeTeamId,
        int AwayTeamId);

    private sealed record MatchRawData(
        DataRow Row,
        ColumnMappings Cols,
        string HomeTeam,
        string AwayTeam,
        DateTime Date,
        TimeSpan Time,
        string LeagueName);
}
