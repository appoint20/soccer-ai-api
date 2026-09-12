using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet;
using SoccerAi.Infrastructure.MlNet.Models;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.MlNet;

public sealed class GoalRateModelStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "soccer-model-transfer-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly HybridModelOptions _options = new();
    private readonly DixonColesOptions _dc = new();
    public GoalRateModelStoreTests()
    {
        var name = Guid.NewGuid().ToString();
        _services = new ServiceCollection().AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name))
            .AddScoped<IApplicationDbContext>(s => s.GetRequiredService<ApplicationDbContext>()).BuildServiceProvider();
    }
    private GoalRateModelStore Store() => new(_services.GetRequiredService<IServiceScopeFactory>(), Options.Create(_options),
        Options.Create(_dc), NullLogger<GoalRateModelStore>.Instance);

    [Fact]
    public async Task Separate_worker_and_api_directories_share_a_verified_loadable_generation()
    {
        var worker = Path.Combine(_root, "worker"); var api = Path.Combine(_root, "api");
        var generation = await Bundle(worker);
        using var publisher = Store();
        await publisher.PublishCurrentAsync(worker, CancellationToken.None);
        await publisher.PublishCurrentAsync(worker, CancellationToken.None); // Idempotent retry.
        using (var scope = _services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().GoalRateModelGenerations.CountAsync()).Should().Be(1);
        Directory.Delete(worker, true); // No shared filesystem remains.
        using var reader = Store();
        _options.ModelDirectory = api;
        var features = new GoalRateFeatureBuilder(Options.Create(_dc), NullLogger<GoalRateFeatureBuilder>.Instance);
        using var forecaster = new GoalRateForecaster(NullLogger<GoalRateForecaster>.Instance, features,
            _services.GetRequiredService<IServiceScopeFactory>(), Options.Create(_options), Options.Create(_dc), reader);
        var forecast = await forecaster.ForecastAsync(new Fixture { Id = 7, LeagueId = 39, HomeTeamId = 1, AwayTeamId = 2,
            Status = "NS", Date = DateTimeOffset.UtcNow.AddDays(1) });
        forecast.Should().NotBeNull();
        forecast!.ModelVersion.Should().EndWith(generation);
        forecast.Probabilities.Over25.Should().BeInRange(0, 1);
        (forecast.Probabilities.HomeWin + forecast.Probabilities.Draw + forecast.Probabilities.AwayWin).Should().BeApproximately(1, 1e-9);
        forecaster.IsAvailable.Should().BeTrue();
        forecaster.ModelVersion.Should().EndWith(generation);
        File.Exists(Path.Combine(api, GoalRateArtifact.PointerFile)).Should().BeTrue();
    }

    [Fact]
    public async Task Rejected_evaluation_and_corrupted_bytes_cannot_be_published_or_replace_the_api_pointer()
    {
        var worker = Path.Combine(_root, "worker"); var api = Path.Combine(_root, "api");
        var generation = await Bundle(worker, accepted: false);
        using var publisher = Store();
        var publish = () => publisher.PublishCurrentAsync(worker, CancellationToken.None);
        await publish.Should().ThrowAsync<InvalidDataException>();
        var evaluation = Path.Combine(worker, "goal-rate-generations", generation, "evaluation.json");
        await File.WriteAllTextAsync(evaluation, JsonSerializer.Serialize(new GoalRateEvaluation {
            PublicationGatePassed = true, PredictionRecipe = GoalRateEnsemble.Recipe }));
        await publisher.PublishCurrentAsync(worker, CancellationToken.None);
        using (var reader = Store()) await reader.RestoreLatestAsync(api, CancellationToken.None);
        var pointerBefore = await File.ReadAllTextAsync(Path.Combine(api, GoalRateArtifact.PointerFile));
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.GoalRateModelGenerations.SingleAsync();
            row.HomeModel = [1, 2, 3]; await db.SaveChangesAsync();
        }
        // A new container must reject the damaged bundle; the old cache remains usable.
        var freshApi = Path.Combine(_root, "fresh-api");
        using (var reader = Store()) await reader.RestoreLatestAsync(freshApi, CancellationToken.None);
        File.Exists(Path.Combine(freshApi, GoalRateArtifact.PointerFile)).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(api, GoalRateArtifact.PointerFile))).Should().Be(pointerBefore);
    }

    private async Task<string> Bundle(string root, bool accepted = true)
    {
        // Small deterministic test transformers exercise real ML.NET save/load
        // and the serving contract; they make no claim about predictive skill.
        var ml = new MLContext(seed: 42); var generation = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(root, "goal-rate-generations", generation); Directory.CreateDirectory(dir);
        var data = ml.Data.LoadFromEnumerable(new[] { new GoalRateRow { DcLambdaHome = 1.5f, DcLambdaAway = 1 } });
        ml.Model.Save(ml.Transforms.CopyColumns("Score", nameof(GoalRateRow.DcLambdaHome)).Fit(data), data.Schema, Path.Combine(dir, "home.zip"));
        ml.Model.Save(ml.Transforms.CopyColumns("Score", nameof(GoalRateRow.DcLambdaAway)).Fit(data), data.Schema, Path.Combine(dir, "away.zip"));
        await File.WriteAllTextAsync(Path.Combine(dir, "calibration.json"), JsonSerializer.Serialize(new GoalRateCalibration {
            HomeScale = 1, AwayScale = 1, MlWeight = .5, ValidationRows = 100 }));
        var manifest = new GoalRateArtifact { Generation = generation, CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            PredictionRecipe = GoalRateEnsemble.Recipe, Features = GoalRateRow.FeatureColumns(), DixonColes = _dc,
            LambdaMin = _options.LambdaMin, LambdaMax = _options.LambdaMax,
            TrainingThroughUtc = new DateTime(2020, 1, 1), CalibrationFromUtc = new DateTime(2020, 1, 2),
            CalibrationThroughUtc = new DateTime(2020, 2, 1),
            Sha256 = new[] { "home.zip", "away.zip", "calibration.json" }.ToDictionary(n => n, n => GoalRateArtifact.Hash(Path.Combine(dir, n))) };
        await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(Path.Combine(dir, "evaluation.json"), JsonSerializer.Serialize(new GoalRateEvaluation {
            PublicationGatePassed = accepted, PredictionRecipe = GoalRateEnsemble.Recipe }));
        await File.WriteAllTextAsync(Path.Combine(root, GoalRateArtifact.PointerFile), JsonSerializer.Serialize(new GoalRateGenerationPointer(generation)));
        return generation;
    }

    public void Dispose() { _services.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
