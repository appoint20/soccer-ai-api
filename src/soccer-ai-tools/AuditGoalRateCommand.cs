using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet;

namespace SoccerAi.Tools;

/// <summary>Offline evaluation deliberately bypasses host creation, database migrations and provider clients.</summary>
public static class AuditGoalRateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var input = CommandArgs.String(args, "--input") ?? throw new ArgumentException("--input=fixtures.json is required");
        var output = CommandArgs.String(args, "--output") ?? throw new ArgumentException("--output=directory is required");
        var settingsFile = CommandArgs.String(args, "--settings");
        using var settings = JsonDocument.Parse(settingsFile is null ? "{}" : await File.ReadAllTextAsync(settingsFile));
        T Read<T>(string section) where T : new() => settings.RootElement.TryGetProperty(section, out var s)
            ? s.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new() : new();
        var bytes = await File.ReadAllBytesAsync(input);
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(bytes, FootballAuditJson.Options) ?? throw new ArgumentException("Invalid fixtures JSON");
        using var services = new ServiceCollection().AddLogging(b => b.AddSimpleConsole()).BuildServiceProvider();
        var dc = Read<DixonColesOptions>("DixonColes"); var hybrid = Read<HybridModelOptions>("HybridModel"); var confluence = Read<ConfluenceOptions>("Confluence");
        var builder = new GoalRateFeatureBuilder(Options.Create(dc), services.GetRequiredService<ILogger<GoalRateFeatureBuilder>>());
        var trainer = new GoalRateTrainingService(services.GetRequiredService<ILogger<GoalRateTrainingService>>(), builder,
            Options.Create(hybrid), Options.Create(confluence), services.GetRequiredService<IServiceScopeFactory>());
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "input-provenance.json"), JsonSerializer.Serialize(new
        {
            Input = Path.GetFullPath(input), Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Fixtures = fixtures.Count, Finished = fixtures.Count(f => f.Status == "FT"),
            Schema = GoalRateFeatureBuilder.SchemaVersion, DixonColes = dc, HybridModel = hybrid, Confluence = confluence
        }, new JsonSerializerOptions { WriteIndented = true }));
        var report = await trainer.TrainAndEvaluateAsync(fixtures, output, publish: false);
        if (report is null) return 2;
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
