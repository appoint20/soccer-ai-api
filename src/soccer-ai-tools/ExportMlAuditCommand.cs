using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.Tools;

/// <summary>Exports football evidence only, using a read-only repeatable-read transaction. No app host or migrations.</summary>
public static class ExportMlAuditCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = CommandArgs.String(args, "--settings") ?? throw new ArgumentException("--settings is required");
        var output = CommandArgs.String(args, "--output") ?? throw new ArgumentException("--output is required");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Use an empty output directory to prevent mixing exports.");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(settings));
        var raw = config.RootElement.GetProperty("ConnectionStrings").GetProperty("PostgresConnection").GetString();
        var cs = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(raw))
        {
            Timeout = 15, CommandTimeout = 90, Pooling = false,
            Options = "-c default_transaction_read_only=on -c statement_timeout=90000",
            ApplicationName = "soccer-ml-read-only-audit", IncludeErrorDetail = false
        };
        try
        {
            await using var connection = new NpgsqlConnection(cs.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await using (var guard = new NpgsqlCommand("SHOW transaction_read_only", connection, transaction))
                if (!string.Equals((string?)await guard.ExecuteScalarAsync(), "on", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Read-only guard failed");
            Directory.CreateDirectory(output);
            var files = new Dictionary<string, object>();
            var queries = new Dictionary<string, string>
            {
                ["fixtures"] = "SELECT row_to_json(f)::text FROM \"Fixtures\" f ORDER BY f.\"Date\", f.\"Id\"",
                ["ai-analyses"] = """
                    SELECT row_to_json(a)::text FROM (
                      SELECT "Id", "FixtureId", "Lang", "HomeProb", "DrawProb", "AwayProb", "Over25Prob", "BttsProb", "Goals23Prob",
                        "AiOver25Qualified", "AiBttsQualified", "AiUnder25Qualified", "AiGoals23Qualified", "AiHomeWinQualified",
                        "AiAwayWinQualified", "AiOverallConfidence", "CreatedAt", "UpdatedAt", "SnapshotJson"
                      FROM "FixtureAnalyses" WHERE "AiOverallConfidence" > 0
                      ORDER BY "FixtureId", "Lang"
                    ) a
                    """
            };
            await using (var exists = new NpgsqlCommand("SELECT to_regclass('\"PredictionSnapshots\"') IS NOT NULL", connection, transaction))
                if ((bool)(await exists.ExecuteScalarAsync())!)
                    queries["prediction-snapshots"] = "SELECT row_to_json(p)::text FROM \"PredictionSnapshots\" p ORDER BY p.\"Id\"";
            foreach (var (name, sql) in queries)
            {
                var path = Path.Combine(output, name + ".json"); var count = 0;
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                await using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    await writer.WriteAsync("[");
                    while (await reader.ReadAsync())
                    {
                        if (count++ > 0) await writer.WriteAsync(",");
                        await writer.WriteAsync(reader.GetString(0));
                    }
                    await writer.WriteAsync("]");
                }
                files[name] = new { Rows = count, Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant() };
            }
            await transaction.RollbackAsync();
            var metadata = new { CapturedAtUtc = DateTimeOffset.UtcNow, Source = "configured_postgresql", ReadOnly = true,
                Isolation = "repeatable_read", Files = files };
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(output, "export-provenance.json"), json);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            // Credentials, host names and provider diagnostics are not printed.
            var causes = new List<string>();
            for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
                causes.Add(cause is System.Net.Sockets.SocketException socket ? $"SocketException:{socket.SocketErrorCode}"
                    : cause is PostgresException postgres ? $"PostgresException:{postgres.SqlState}" : cause.GetType().Name);
            Console.Error.WriteLine($"Read-only export failed ({string.Join(" → ", causes)}). No migrations or writes were run.");
            return 1;
        }
    }
}
