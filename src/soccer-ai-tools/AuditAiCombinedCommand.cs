using System.Text.Json;
using System.Security.Cryptography;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Services.Statistics;

namespace SoccerAi.Tools;

/// <summary>Uses frozen exported records only. Never regenerates historical AI decisions.</summary>
public static class AuditAiCombinedCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var input = CommandArgs.String(args, "--input-dir") ?? throw new ArgumentException("--input-dir required");
        var output = CommandArgs.String(args, "--output") ?? throw new ArgumentException("--output required");
        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(input, "export-provenance.json")));
        var files = provenance.RootElement.GetProperty("Files");
        foreach (var name in new[] { "fixtures", "ai-analyses", "prediction-snapshots" })
        {
            var path = Path.Combine(input, name + ".json");
            if (!files.TryGetProperty(name, out var entry))
            {
                if (name != "prediction-snapshots" || File.Exists(path))
                    throw new InvalidDataException($"Missing export provenance for {name}");
                continue; // Table was not present in this exported schema.
            }
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(hash, entry.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Export checksum mismatch for {name}");
            using var contents = JsonDocument.Parse(bytes);
            if (contents.RootElement.GetArrayLength() != entry.GetProperty("Rows").GetInt32())
                throw new InvalidDataException($"Export row count mismatch for {name}");
        }
        async Task<List<T>> Read<T>(string name) => File.Exists(Path.Combine(input, name + ".json"))
            ? JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(Path.Combine(input, name + ".json"))) ?? [] : [];
        var fixtures = await Read<Fixture>("fixtures"); var ai = await Read<FixtureAnalysis>("ai-analyses");
        var snapshots = await Read<PredictionSnapshot>("prediction-snapshots");
        var pairs = PredictionStatisticsService.Pair(fixtures, snapshots);
        var lookup = fixtures.ToDictionary(f => f.Id);
        var legacy = ai.Where(a => a.AiOverallConfidence > 0 && lookup.TryGetValue(a.FixtureId, out var f) &&
                f.Status == "FT" && a.UpdatedAt <= f.Date.AddHours(-1))
            .GroupBy(a => a.FixtureId).Select(g => g.OrderBy(a => a.Lang == "en" ? 0 : 1).ThenByDescending(a => a.UpdatedAt).First()).ToList();
        object Rate(string market, Func<FixtureAnalysis, bool> picked, Func<Fixture, bool> won)
        {
            var choices = legacy.Where(picked).ToList(); var hits = choices.Count(a => won(lookup[a.FixtureId]));
            var (lower, upper) = PredictionStatisticsService.Wilson(hits, choices.Count);
            return new { Market = market, Picks = choices.Count, Correct = hits,
                HitRate = choices.Count == 0 ? (double?)null : (double)hits / choices.Count, Lower95 = lower, Upper95 = upper };
        }
        var report = new { GeneratedAtUtc = DateTimeOffset.UtcNow, InputProvenance = provenance.RootElement, Fixtures = fixtures.Count,
            LatestFinishedKickoff = fixtures.Where(f => f.Status == "FT").Select(f => (DateTimeOffset?)f.Date).Max(),
            AiRows = ai.Count, AiFixtures = ai.Select(a => a.FixtureId).Distinct().Count(),
            ImmutablePredictionRows = snapshots.Count, ScoredPredictionFixtures = pairs.Count,
            Combined = AiComparisonStatistics.Build(pairs),
            LegacyDiagnostic = new { Fixtures = legacy.Count,
                Note = "Mutable cache, latest update at least 1h before kickoff; one language per match. Not an immutable record, not ML+AI accuracy, not causal lift. No model/prompt provenance. Qualification=false is abstention, not a prediction of the opposite result.",
                Markets = new[] { Rate("btts", a => a.AiBttsQualified, f => f.HomeGoal > 0 && f.AwayGoal > 0),
                    Rate("over25", a => a.AiOver25Qualified, f => f.HomeGoal + f.AwayGoal > 2),
                    Rate("under25", a => a.AiUnder25Qualified, f => f.HomeGoal + f.AwayGoal <= 2) } } };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, json); Console.WriteLine(json); return 0;
    }
}
