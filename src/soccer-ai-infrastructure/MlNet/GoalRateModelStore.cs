using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>Transfers accepted bundles through the existing shared database; local files are a verified cache.</summary>
public sealed class GoalRateModelStore(IServiceScopeFactory scopes, IOptions<HybridModelOptions> options,
    IOptions<DixonColesOptions> dc, ILogger<GoalRateModelStore> logger) : IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTimeOffset _nextRefresh;

    public async Task PublishCurrentAsync(string root, CancellationToken ct)
    {
        var pointerPath = Path.Combine(root, GoalRateArtifact.PointerFile);
        if (!File.Exists(pointerPath)) return;
        var pointer = JsonSerializer.Deserialize<GoalRateGenerationPointer>(await File.ReadAllTextAsync(pointerPath, ct));
        if (pointer == null || !Guid.TryParseExact(pointer.Generation, "N", out _))
            throw new InvalidDataException("Invalid model pointer");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        if (await db.GoalRateModelGenerations.AnyAsync(m => m.Generation == pointer.Generation, ct)) return;
        var dir = Path.Combine(root, "goal-rate-generations", pointer.Generation);
        var bundle = new GoalRateModelGeneration
        {
            Generation = pointer.Generation,
            ManifestJson = await File.ReadAllTextAsync(Path.Combine(dir, "manifest.json"), ct),
            CalibrationJson = await File.ReadAllTextAsync(Path.Combine(dir, "calibration.json"), ct),
            EvaluationJson = await File.ReadAllTextAsync(Path.Combine(dir, "evaluation.json"), ct),
            HomeModel = await File.ReadAllBytesAsync(Path.Combine(dir, "home.zip"), ct),
            AwayModel = await File.ReadAllBytesAsync(Path.Combine(dir, "away.zip"), ct)
        };
        var manifest = Validate(bundle);
        bundle.CreatedAtUtc = manifest.CreatedAtUtc;
        db.GoalRateModelGenerations.Add(bundle);
        await db.SaveChangesAsync(ct); // All bytes and metadata become visible together.
        logger.LogInformation("[GoalRate] Published accepted generation {Generation} to shared storage", bundle.Generation);
    }

    public async Task RestoreLatestAsync(string root, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < _nextRefresh) return;
            _nextRefresh = DateTimeOffset.UtcNow.AddMinutes(1);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var latest = await db.GoalRateModelGenerations.AsNoTracking().OrderByDescending(m => m.CreatedAtUtc)
                .ThenByDescending(m => m.Generation).Select(m => new { m.Generation }).FirstOrDefaultAsync(ct);
            if (latest == null) return;
            var pointerPath = Path.Combine(root, GoalRateArtifact.PointerFile);
            // Reading only the ID on ordinary requests avoids repeatedly loading model blobs.
            if (File.Exists(pointerPath))
            {
                try
                {
                    var current = JsonSerializer.Deserialize<GoalRateGenerationPointer>(await File.ReadAllTextAsync(pointerPath, ct));
                    if (current?.Generation == latest.Generation) return;
                }
                catch (JsonException) { /* Repair an incomplete local cache. */ }
            }
            var bundle = await db.GoalRateModelGenerations.AsNoTracking().SingleAsync(m => m.Generation == latest.Generation, ct);
            var manifest = Validate(bundle);
            if (manifest.CreatedAtUtc != bundle.CreatedAtUtc) throw new InvalidDataException("Model publication timestamp mismatch");
            var generationRoot = Path.Combine(root, "goal-rate-generations");
            Directory.CreateDirectory(generationRoot);
            var dir = Path.Combine(generationRoot, bundle.Generation);
            var staging = Path.Combine(generationRoot, bundle.Generation + "." + Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(staging);
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(staging, "home.zip"), bundle.HomeModel, ct);
                await File.WriteAllBytesAsync(Path.Combine(staging, "away.zip"), bundle.AwayModel, ct);
                await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), bundle.ManifestJson, ct);
                await File.WriteAllTextAsync(Path.Combine(staging, "calibration.json"), bundle.CalibrationJson, ct);
                await File.WriteAllTextAsync(Path.Combine(staging, "evaluation.json"), bundle.EvaluationJson, ct);
                if (!Directory.Exists(dir)) Directory.Move(staging, dir);
                // Existing generation directories are immutable; verify before
                // pointing at one left behind by a prior interrupted transfer.
                foreach (var file in new[] { "home.zip", "away.zip", "calibration.json" })
                    if (GoalRateArtifact.Hash(Path.Combine(dir, file)) != manifest.Sha256.GetValueOrDefault(file))
                        throw new InvalidDataException("Local model generation checksum mismatch");
                var temporaryPointer = pointerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporaryPointer, JsonSerializer.Serialize(new GoalRateGenerationPointer(bundle.Generation)), ct);
                    File.Move(temporaryPointer, pointerPath, overwrite: true);
                }
                finally { if (File.Exists(temporaryPointer)) File.Delete(temporaryPointer); }
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            logger.LogInformation("[GoalRate] Downloaded accepted generation {Generation} from shared storage", bundle.Generation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[GoalRate] Shared model refresh failed; retaining verified local model or DC fallback");
        }
        finally { _refreshLock.Release(); }
    }

    private GoalRateArtifact Validate(GoalRateModelGeneration bundle)
    {
        var manifest = JsonSerializer.Deserialize<GoalRateArtifact>(bundle.ManifestJson);
        var evaluation = JsonSerializer.Deserialize<GoalRateEvaluation>(bundle.EvaluationJson);
        if (manifest == null || !Guid.TryParseExact(bundle.Generation, "N", out _) || manifest.Generation != bundle.Generation ||
            !manifest.Supports(dc.Value, options.Value) || manifest.CreatedAtUtc == default || manifest.CreatedAtUtc > DateTimeOffset.UtcNow ||
            evaluation?.PublicationGatePassed != true || evaluation.FeatureSchema != manifest.SchemaVersion ||
            evaluation.PredictionRecipe != manifest.PredictionRecipe)
            throw new InvalidDataException("Model bundle failed compatibility or publication gate checks");
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (Hash(bundle.HomeModel) != manifest.Sha256.GetValueOrDefault("home.zip") ||
            Hash(bundle.AwayModel) != manifest.Sha256.GetValueOrDefault("away.zip") ||
            Hash(Encoding.UTF8.GetBytes(bundle.CalibrationJson)) != manifest.Sha256.GetValueOrDefault("calibration.json"))
            throw new InvalidDataException("Shared model bundle checksum mismatch");
        return manifest;
    }

    public void Dispose() => _refreshLock.Dispose();
}
