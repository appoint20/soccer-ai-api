using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet;

namespace SoccerAi.Tools;

public static class AuditSourceGoalsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var input = CommandArgs.String(args, "--input") ?? throw new ArgumentException("--input required");
        var output = CommandArgs.String(args, "--output") ?? throw new ArgumentException("--output required");
        var settingsPath = CommandArgs.String(args, "--settings") ?? throw new ArgumentException("--settings required");
        var from = CommandArgs.Date(args, "--from") ?? throw new ArgumentException("--from required");
        var until = CommandArgs.Date(args, "--until") ?? throw new ArgumentException("--until required");
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        T Read<T>(string section) where T : new() => settings.RootElement.TryGetProperty(section, out var value)
            ? value.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new() : new();
        var bytes = await File.ReadAllBytesAsync(input);
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(bytes, FootballAuditJson.Options)
            ?? throw new InvalidDataException("Invalid fixtures export.");
        foreach (var fixture in fixtures)
        {
            fixture.HomeWinOdds = fixture.DrawOdds = fixture.AwayWinOdds = null;
            fixture.Over25Odds = fixture.Under25Odds = fixture.BttsYesOdds = null;
            fixture.OddsCheckedAtUtc = fixture.OddsUpdatedAtUtc = null;
        }
        using var services = new ServiceCollection().AddLogging(builder => builder.AddSimpleConsole()).BuildServiceProvider();
        var dc = Read<DixonColesOptions>("DixonColes");
        var hybrid = Read<HybridModelOptions>("HybridModel");
        hybrid.AllowOfflineTrainerFallback = CommandArgs.Flag(args, "--allow-trainer-fallback");
        var builder = new GoalRateFeatureBuilder(Options.Create(dc), services.GetRequiredService<ILogger<GoalRateFeatureBuilder>>());
        var trainer = new GoalRateTrainingService(services.GetRequiredService<ILogger<GoalRateTrainingService>>(), builder,
            Options.Create(hybrid), Options.Create(Read<ConfluenceOptions>("Confluence")), services.GetRequiredService<IServiceScopeFactory>());
        var report = trainer.ForecastHoldout(fixtures, from, until);
        var document = new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            InputSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Protocol = "Retrospective component experiment. Fixed model fitting and disjoint calibration strictly before --from. " +
                       "Rolling features use results from previous UTC days only. All bookmaker features masked for every row. " +
                       "No database writes, model publication or provider calls. Historical feature availability is reconstructed.",
            DixonColes = dc, HybridModel = hybrid, Report = report
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {report.Predictions.Count} {report.Trainer} forecasts; fit {report.TrainingRows}, calibration {report.CalibrationRows}.");
        return 0;
    }
}
