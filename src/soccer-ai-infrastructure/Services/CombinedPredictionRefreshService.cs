using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Forecasts;

namespace SoccerAi.Infrastructure.Services;

public sealed class CombinedPredictionRefreshService(
    IApplicationDbContext db, IDixonColesModel historicalModel, IGoalRateForecaster mlModel,
    IApiFootballService provider, IMatchForecastService aiForecasts, IMatchDataProvider matchData,
    ICombinedPredictionService combiner, IAnalysisPrecomputeService precompute, IAiSyncService narration,
    IOptions<CombinedPredictionOptions> options, ILogger<CombinedPredictionRefreshService> logger)
    : ICombinedPredictionRefreshService
{
    public async Task<CombinedPredictionRefreshResult> RefreshAsync(int fixtureId, bool refreshNarration = true,
        CancellationToken ct = default)
    {
        var fixture = await db.Fixtures.AsNoTracking().SingleOrDefaultAsync(value => value.Id == fixtureId, ct)
            ?? throw new KeyNotFoundException("Fixture not found.");
        RequireUpcoming(fixture);
        var teams = await db.Teams.AsNoTracking().Where(team => team.ApiId == fixture.HomeTeamId || team.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(team => team.ApiId, ct);
        if (!teams.TryGetValue(fixture.HomeTeamId, out var home) || !teams.TryGetValue(fixture.AwayTeamId, out var away))
            throw new InvalidOperationException("Both teams must be synced before refreshing a prediction.");
        var warnings = new List<string>();
        async Task<T?> Read<T>(string name, Func<Task<T?>> read) where T : class
        {
            try { return await read(); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "[CombinedPrediction] {Source} failed for fixture {FixtureId}", name, fixtureId);
                warnings.Add($"{name} unavailable; no stale substitute was used.");
                return null;
            }
        }
        var history = await Read("historical", () => historicalModel.CalculateProbabilitiesAsync(
            fixture.LeagueId, fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, ct));
        var historicalAt = DateTimeOffset.UtcNow;
        var learned = await Read("ml", () => mlModel.ForecastAsync(fixture, ct));
        var learnedAt = DateTimeOffset.UtcNow;
        var providerPrediction = await Read("provider", () => provider.GetPredictionAsync(fixture.ApiId, ct));
        var providerAt = DateTimeOffset.UtcNow;
        var sources = new List<PredictionSource>
        {
            new("historical", "dixon_coles", history is null ? null : CombinedMarkets.From(history),
                history is null ? "Insufficient historical data or historical model unavailable." : null, CapturedAtUtc: historicalAt),
            new("ml", "raw_learned_goal_rates", learned?.RawMlProbabilities is { } raw ? CombinedMarkets.From(raw) : null,
                learned?.RawMlProbabilities is null ? "No accepted model with separate raw ML probabilities is available." : null,
                learned?.ModelVersion, CapturedAtUtc: learnedAt),
            new("provider", "provider_1x2_and_poisson_from_recent_goals", CombinedPredictionService.ProviderMarkets(providerPrediction),
                providerPrediction is null ? "Provider returned no prediction." : null, CapturedAtUtc: providerAt),
            new("ai", "contextual_ai", UnavailableReason: "AI has not run yet.", ModelVersion: options.Value.AiModel)
        };
        var provisional = combiner.Combine(fixtureId, fixture.Date, DateTimeOffset.UtcNow, sources);
        var context = await matchData.LoadAsync(fixture, ct);
        var probabilities = provisional.Markets;
        var request = new MatchAnalysis
        {
            Id = fixture.Id, Date = fixture.Date, Status = fixture.Status,
            HomeTeam = home.Name, AwayTeam = away.Name, League = LeagueCatalog.Name(fixture.LeagueId),
            HomeStats = context.TeamStats.Home, AwayStats = context.TeamStats.Away, Provider = providerPrediction,
            H2H = context.H2H, CombinedPrediction = provisional,
            Prediction = new PredictionResponse
            {
                HomeWin = new() { Probability = probabilities.HomeWin!.Value },
                Draw = new() { Probability = probabilities.Draw!.Value },
                AwayWin = new() { Probability = probabilities.AwayWin!.Value },
                BTTS = new() { Probability = probabilities.Btts!.Value },
                Over25 = new() { Probability = probabilities.Over25!.Value },
                TwoToThreeGoals = new() { Probability = probabilities.TwoToThreeGoals!.Value }
            }
        };
        var ai = await Read("ai", () => aiForecasts.ForecastCombinedAsync(request, options.Value.AiModel, ct));
        sources[3] = new("ai", "contextual_ai", ai?.Markets,
            ai?.Markets is null ? "AI disabled, missing credentials, failed request or invalid response." : null,
            ai?.Model ?? options.Value.AiModel, ai?.Rationale, DateTimeOffset.UtcNow);
        var latestFixture = await db.Fixtures.AsNoTracking().SingleAsync(value => value.Id == fixtureId, ct);
        RequireUpcoming(latestFixture);
        if (latestFixture.Date != fixture.Date || latestFixture.HomeTeamId != fixture.HomeTeamId || latestFixture.AwayTeamId != fixture.AwayTeamId)
            throw new InvalidOperationException("Fixture changed during prediction; retry with the new context.");
        var combined = combiner.Combine(fixtureId, fixture.Date, DateTimeOffset.UtcNow, sources) with
        {
            HomeTeamId = fixture.HomeTeamId, AwayTeamId = fixture.AwayTeamId
        };
        foreach (var source in combined.Sources.Where(source => source.OutcomeWeight == 0 || source.GoalsWeight == 0))
            warnings.Add($"{source.Name}: {source.UnavailableReason ?? "Missing/invalid markets or zero configured weight."}");
        warnings.Add("Combination weights and the provider goals adapter are experimental, not validated accuracy improvements.");
        var saved = new CombinedPredictionSnapshot
        {
            FixtureId = fixtureId, KickoffUtc = fixture.Date, CapturedAtUtc = combined.CapturedAtUtc,
            PredictionJson = JsonSerializer.Serialize(combined),
            EvidenceJson = JsonSerializer.Serialize(new
            {
                provider = providerPrediction, provider_captured_at_utc = providerAt,
                ai_raw_response = ai?.RawResponseJson, ai_input_hash = ai?.InputHash, input = request
            })
        };
        db.CombinedPredictionSnapshots.Add(saved);
        if (providerPrediction is not null)
        {
            var row = await db.FixturePredictions.SingleOrDefaultAsync(value => value.FixtureId == fixtureId, ct);
            if (row is null)
            {
                row = new FixturePrediction { FixtureId = fixtureId };
                db.FixturePredictions.Add(row);
            }
            FixtureSyncService.Apply(row, providerPrediction, providerAt);
        }
        await db.SaveChangesAsync(ct);
        var snapshots = await precompute.RecomputeFixtureAsync(fixtureId, ct);
        if (!snapshots.ContainsKey("en") || !snapshots.ContainsKey("de"))
            throw new InvalidOperationException("Prediction was stored, but analysis snapshots could not be refreshed. Retry the refresh.");
        var narrated = false;
        if (refreshNarration)
        {
            try
            {
                await narration.SyncSingleFixtureAsync(fixtureId, force: true, cancellationToken: ct);
                narrated = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "[CombinedPrediction] Narration failed for fixture {FixtureId}", fixtureId);
                warnings.Add("Combined probabilities were saved, but fresh narration failed. Check AI credentials, quota and server logs.");
            }
        }
        return new(fixtureId, saved.Id, combined, narrated, warnings);
    }

    private static void RequireUpcoming(Fixture fixture)
    {
        if (fixture.Status != "NS" || fixture.Date <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Only not-started future fixtures can receive new pre-match predictions.");
    }
}
