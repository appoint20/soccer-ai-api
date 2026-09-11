using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

public sealed class GoalRatePrediction { public float Score { get; set; } }

/// <summary>Serves a verified immutable generation; missing/invalid models use the caller's DC fallback.</summary>
public sealed class GoalRateForecaster(
    ILogger<GoalRateForecaster> logger,
    GoalRateFeatureBuilder featureBuilder,
    IServiceScopeFactory scopeFactory,
    IOptions<HybridModelOptions> options,
    IOptions<DixonColesOptions> dixonColesOptions) : IGoalRateForecaster, IDisposable
{
    private readonly HybridModelOptions _opt = options.Value;
    private readonly DixonColesOptions _dc = dixonColesOptions.Value;
    private readonly MLContext _ml = new(seed: 42);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private ModelState? _state;
    private HistoryState? _history;
    private string? _attemptedPointer;
    private sealed record ModelState(ITransformer Home, ITransformer Away,
        GoalRateCalibration Calibration, GoalRateArtifact Manifest);
    private sealed record HistoryState(List<Fixture> Fixtures, DateTimeOffset CapturedAt);

    public bool IsAvailable => Volatile.Read(ref _state) is not null;
    public string? ModelVersion => Volatile.Read(ref _state) is { } state
        ? $"{state.Manifest.SchemaVersion}:{state.Manifest.Generation}" : null;

    public void Reload()
    {
        Volatile.Write(ref _attemptedPointer, null);
        Volatile.Write(ref _history, null);
    }

    public async Task<GoalRateForecast?> ForecastAsync(Fixture fixture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return (await ForecastManyAsync([fixture], ct)).GetValueOrDefault(fixture.Id);
    }

    public async Task<IReadOnlyDictionary<int, GoalRateForecast>> ForecastManyAsync(
        IReadOnlyCollection<Fixture> fixtures, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        if (fixtures.Count == 0 || !_opt.Enabled) return new Dictionary<int, GoalRateForecast>();
        await EnsureLoadedAsync(ct);
        var state = Volatile.Read(ref _state);
        if (state is null) return new Dictionary<int, GoalRateForecast>();
        // A model fitted/calibrated on a fixture's result cannot backtest it.
        var eligible = fixtures.Where(f => state.Manifest.CanScore(f.Date.UtcDateTime)).ToList();
        if (eligible.Count == 0) return new Dictionary<int, GoalRateForecast>();
        var history = await GetHistoryAsync(ct);
        var ids = eligible.Select(f => f.Id).ToHashSet();
        var combined = history.Fixtures.Where(f => !ids.Contains(f.Id)).Concat(eligible);
        var rows = featureBuilder.Build(combined).Where(r => ids.Contains((int)r.FixtureId)).ToList();
        var view = _ml.Data.LoadFromEnumerable(rows);
        var home = state.Home.Transform(view).GetColumn<float>("Score").ToArray();
        var away = state.Away.Transform(view).GetColumn<float>("Score").ToArray();
        if (home.Length != rows.Count || away.Length != rows.Count)
            throw new InvalidDataException("Goal-rate prediction row mismatch");
        var output = new Dictionary<int, GoalRateForecast>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!float.IsFinite(home[i]) || !float.IsFinite(away[i])) continue;
            var h = Math.Clamp(home[i] * state.Calibration.HomeScale, _opt.LambdaMin, _opt.LambdaMax);
            var a = Math.Clamp(away[i] * state.Calibration.AwayScale, _opt.LambdaMin, _opt.LambdaMax);
            var m = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(h, a, _dc.Rho, _dc.MaxGoals));
            if (state.Manifest.PredictionRecipe == GoalRateEnsemble.Recipe && rows[i].DcLambdaSum > 0)
            {
                var dc = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(
                    rows[i].DcLambdaHome, rows[i].DcLambdaAway, _dc.Rho, _dc.MaxGoals));
                m = GoalRateEnsemble.Mix(m, dc, state.Calibration.MlWeight);
                h = state.Calibration.MlWeight * h + (1 - state.Calibration.MlWeight) * rows[i].DcLambdaHome;
                a = state.Calibration.MlWeight * a + (1 - state.Calibration.MlWeight) * rows[i].DcLambdaAway;
            }
            output[(int)rows[i].FixtureId] = new GoalRateForecast(h, a, new PoissonProbabilities
            {
                HomeWin = m.HomeWin, Draw = m.Draw, AwayWin = m.AwayWin,
                Over25 = m.Over25, BothTeamScoredGoal = m.Btts, TwoToThreeGoals = m.TwoToThreeGoals,
                BttsAndOver25 = m.BttsAndOver25, HomeExpectedGoals = h, AwayExpectedGoals = a
            }, $"{state.Manifest.SchemaVersion}:{state.Manifest.Generation}");
        }
        return output;
    }

    private async Task<HistoryState> GetHistoryAsync(CancellationToken ct)
    {
        var cached = Volatile.Read(ref _history);
        if (cached is not null && DateTimeOffset.UtcNow - cached.CapturedAt < TimeSpan.FromMinutes(10)) return cached;
        await _cacheLock.WaitAsync(ct);
        try
        {
            cached = Volatile.Read(ref _history);
            if (cached is not null && DateTimeOffset.UtcNow - cached.CapturedAt < TimeSpan.FromMinutes(10)) return cached;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var fixtures = await db.Fixtures.AsNoTracking().Where(f => f.Status == "FT")
                .OrderBy(f => f.Date).ToListAsync(ct);
            cached = new HistoryState(fixtures, DateTimeOffset.UtcNow);
            Volatile.Write(ref _history, cached);
            return cached;
        }
        finally { _cacheLock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), _opt.ModelDirectory);
        var pointerPath = Path.Combine(root, GoalRateArtifact.PointerFile);
        if (!File.Exists(pointerPath)) return; // Older unversioned ZIPs are not certified for this feature contract.
        var pointerText = await File.ReadAllTextAsync(pointerPath, ct);
        if (Volatile.Read(ref _attemptedPointer) == pointerText) return;
        await _loadLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _attemptedPointer) == pointerText) return;
            var pointer = JsonSerializer.Deserialize<GoalRateGenerationPointer>(pointerText)
                ?? throw new InvalidDataException("Missing generation pointer");
            if (!Guid.TryParseExact(pointer.Generation, "N", out _)) throw new InvalidDataException("Invalid generation ID");
            var dir = Path.Combine(root, "goal-rate-generations", pointer.Generation);
            var manifest = JsonSerializer.Deserialize<GoalRateArtifact>(await File.ReadAllTextAsync(Path.Combine(dir, "manifest.json"), ct))
                ?? throw new InvalidDataException("Missing generation manifest");
            if (manifest.Generation != pointer.Generation || !manifest.Supports(_dc, _opt))
                throw new InvalidDataException("Model schema/configuration or calibration boundaries mismatch");
            foreach (var file in new[] { "home.zip", "away.zip", "calibration.json" })
                if (manifest.Sha256.GetValueOrDefault(file) != GoalRateArtifact.Hash(Path.Combine(dir, file)))
                    throw new InvalidDataException($"Model checksum mismatch: {file}");
            var correction = JsonSerializer.Deserialize<GoalRateCalibration>(await File.ReadAllTextAsync(Path.Combine(dir, "calibration.json"), ct));
            if (correction is null || !double.IsFinite(correction.HomeScale) || !double.IsFinite(correction.AwayScale) ||
                !double.IsFinite(correction.MlWeight) || correction.MlWeight is < 0 or > 1 ||
                correction.HomeScale is < 0.5 or > 2 || correction.AwayScale is < 0.5 or > 2 || correction.ValidationRows <= 0)
                throw new InvalidDataException("Invalid or missing held-out calibration");
            var next = new ModelState(_ml.Model.Load(Path.Combine(dir, "home.zip"), out _),
                _ml.Model.Load(Path.Combine(dir, "away.zip"), out _), correction, manifest);
            Volatile.Write(ref _state, next);
            Volatile.Write(ref _history, null);
            Volatile.Write(ref _attemptedPointer, pointerText);
            logger.LogInformation("[GoalRate] Loaded verified generation {Generation}", manifest.Generation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous verified pair, if present. Never partially load a pair.
            Volatile.Write(ref _attemptedPointer, pointerText);
            logger.LogError(ex, "[GoalRate] Rejected invalid generation; retaining previous verified model or DC fallback");
        }
        finally { _loadLock.Release(); }
    }

    public void Dispose() { _loadLock.Dispose(); _cacheLock.Dispose(); }
}
