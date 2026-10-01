using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using SoccerAi.Api.Automation;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Statistics;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;
using soccer_ai_unit_tests.Persistence;

namespace soccer_ai_unit_tests.EndToEnd;

[Trait("Category", "EndToEnd")]
public class CombinedPredictionEndToEndTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [LocalPostgresFact]
    public async Task PostgresStartupMigratesAndPersistsTheHttpRefresh()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SOCCER_TEST_POSTGRES"));
        if (connection.Host is not ("localhost" or "127.0.0.1") || connection.Database != "soccer_worker_regression")
            throw new InvalidOperationException("Only the dedicated local soccer_worker_regression database is allowed.");
        var schema = "soccer_combined_e2e_" + Guid.NewGuid().ToString("N");
        connection.SearchPath = schema;
        connection.Pooling = false;
        await using var control = new NpgsqlConnection(connection.ConnectionString);
        await control.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", control))
            await create.ExecuteNonQueryAsync();
        try
        {
            using var storage = new TestStorage();
            await using var app = new TestApp(storage.Database, connection.ConnectionString) { EnableAutomation = true };
            using var client = await app.StartAsync();
            var job = await RefreshAsync(client, 1, true);
            job.State.Should().Be("completed");
            foreach (var language in new[] { "en", "de" })
                AssertMarkets(await ReadMatchAsync(client, 1, language), job.Result!.Prediction);
            using var scope = app.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await database.Database.GetAppliedMigrationsAsync()).Should().Contain("20260930220000_AddCombinedPredictionSnapshotsPostgres");
            var saved = await database.CombinedPredictionSnapshots.SingleAsync();
            saved.Id.Should().Be(job.Result!.SnapshotId);
            saved.CapturedAtUtc.Should().BeCloseTo(job.Result.Prediction.CapturedAtUtc, TimeSpan.FromMicroseconds(1));
            (await database.FixtureAnalyses.CountAsync()).Should().Be(2);
            await database.Fixtures.Where(fixture => fixture.Id == 2 || fixture.Id == 3)
                .ExecuteUpdateAsync(update => update.SetProperty(fixture => fixture.Date, DateTimeOffset.UtcNow.AddHours(4)));
            var automated = await scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomation>().RunAsync();
            automated.Completed.Should().Be(2);
            (await database.CombinedPredictionAutomationAttempts.CountAsync()).Should().Be(2);
            (await database.Database.GetAppliedMigrationsAsync()).Should().Contain("20261001180000_AddCombinedPredictionAutomationPostgres");
            var kickoff = await database.Fixtures.Where(fixture => fixture.Id == 2).Select(fixture => fixture.Date).SingleAsync();
            async Task<bool> Reserve(int fixtureId)
            {
                using var concurrentScope = app.Services.CreateScope();
                return await concurrentScope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomationStore>().TryReserveAsync(new()
                {
                    FixtureId = fixtureId, KickoffUtc = kickoff, StartedAtUtc = DateTimeOffset.UtcNow, Window = "concurrency-probe"
                }, 10, default);
            }
            var reservations = await Task.WhenAll(Task.Run(() => Reserve(2)), Task.Run(() => Reserve(3)));
            reservations.Count(value => value).Should().Be(1);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", control);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ScheduledRefreshPersistsBilingualAnalysesAndAdminOnlyAttemptHistory()
    {
        using var storage = new TestStorage();
        await using var app = new TestApp(storage.Database) { EnableAutomation = true };
        using var client = await app.StartAsync();
        using var anonymous = app.CreateClient();
        (await anonymous.GetAsync("/api/automation/predictions/scheduled")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Fixtures.Where(fixture => fixture.Status == "NS")
            .ExecuteUpdateAsync(update => update.SetProperty(fixture => fixture.Date, DateTimeOffset.UtcNow.AddHours(4)));
        var automation = scope.ServiceProvider.GetRequiredService<ICombinedPredictionAutomation>();
        (await automation.RunAsync()).Completed.Should().Be(3);
        (await automation.RunAsync()).Attempted.Should().Be(0);
        app.AiHttp.Calls.Should().Be(3);
        app.NarrationInputs.Should().HaveCount(3);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/automation/predictions/scheduled");
        var attempts = history.GetProperty("data").GetProperty("attempts").EnumerateArray().ToList();
        attempts.Should().HaveCount(3);
        foreach (var attempt in attempts)
        {
            attempt.GetProperty("status").GetString().Should().Be("completed");
            var fixtureId = attempt.GetProperty("fixture_id").GetInt32();
            var saved = await database.CombinedPredictionSnapshots.SingleAsync(snapshot => snapshot.FixtureId == fixtureId);
            var combined = JsonSerializer.Deserialize<CombinedPrediction>(saved.PredictionJson)!;
            foreach (var language in new[] { "en", "de" })
            {
                var match = await ReadMatchAsync(client, fixtureId, language);
                AssertMarkets(match, combined);
                match.Ai!.Analysis.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RefreshServesBothLanguagesAndKeepsImmutableVersionsAcrossRestart(int fixtureId)
    {
        using var storage = new TestStorage();
        Guid latestVersion;
        DateTimeOffset latestCapture;
        await using (var app = new TestApp(storage.Database))
        {
            using var client = await app.StartAsync();
            var first = await RefreshAsync(client, fixtureId, true);
            first.State.Should().Be("completed");
            first.Result!.NarrationRefreshed.Should().BeTrue();
            var combined = first.Result.Prediction;
            combined.AllFourSourcesAvailable.Should().BeTrue();
            combined.Sources.Select(source => source.Name).Should().Equal("historical", "ml", "provider", "ai");
            combined.Sources.Select(source => source.GoalsWeight).Should().Equal(.45, .30, .15, .10);
            combined.Markets.Btts.Should().BeApproximately(combined.Sources.Sum(source => source.Markets!.Btts!.Value * source.GoalsWeight), 1e-12);
            combined.Markets.HasGoals.Should().BeTrue();
            combined.Markets.HasOutcomes.Should().BeTrue();
            app.NarrationInputs.Single().CombinedPrediction!.CapturedAtUtc.Should().Be(combined.CapturedAtUtc);

            foreach (var language in new[] { "en", "de" })
            {
                var match = await ReadMatchAsync(client, fixtureId, language);
                AssertMarkets(match, combined);
                match.Ai!.Analysis.Should().Contain(language == "en" ? "English" : "Deutsche");
                match.Ai.GeneratedAtUtc.Should().BeOnOrAfter(combined.CapturedAtUtc);
            }

            using var firstScope = app.Services.CreateScope();
            var database = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await database.CombinedPredictionSnapshots.AsNoTracking().SingleAsync();
            var firstJson = saved.PredictionJson;
            saved.EvidenceJson.Should().Contain("ai_raw_response").And.Contain("choices");
            using var evidence = JsonDocument.Parse(saved.EvidenceJson);
            evidence.RootElement.GetProperty("ai_input_hash").GetString().Should().HaveLength(64);
            var ledger = await database.PredictionSnapshots.AsNoTracking().Select(row => row.ContextJson).ToListAsync();
            (await database.Database.GetAppliedMigrationsAsync()).Should().Contain("20260930220001_AddCombinedPredictionSnapshots");

            var second = await RefreshAsync(client, fixtureId, false);
            second.State.Should().Be("completed");
            second.RefreshNarration.Should().BeFalse();
            second.Result!.NarrationRefreshed.Should().BeFalse();
            second.Result.SnapshotId.Should().NotBe(first.Result.SnapshotId);
            app.NarrationInputs.Should().HaveCount(1);
            app.AiHttp.Calls.Should().Be(2);
            app.Provider.Verify(service => service.GetPredictionAsync(100 + fixtureId, It.IsAny<CancellationToken>()), Times.Exactly(2));
            (await database.CombinedPredictionSnapshots.CountAsync()).Should().Be(2);
            (await database.CombinedPredictionSnapshots.AsNoTracking().SingleAsync(row => row.Id == saved.Id)).PredictionJson.Should().Be(firstJson);
            (await database.PredictionSnapshots.AsNoTracking().Select(row => row.ContextJson).ToListAsync()).Should().Equal(ledger);
            (await database.FixtureAnalyses.CountAsync()).Should().Be(2);
            latestVersion = second.Result.SnapshotId;
            latestCapture = second.Result.Prediction.CapturedAtUtc;
            var withoutFreshNarration = await ReadMatchAsync(client, fixtureId, "en");
            withoutFreshNarration.Ai?.Analysis.Should().BeNullOrEmpty();
            withoutFreshNarration.Ai?.GeneratedAtUtc.Should().BeNull();
        }

        await using var restarted = new TestApp(storage.Database);
        using var restartedClient = await restarted.StartAsync();
        var restored = await ReadMatchAsync(restartedClient, fixtureId, "de");
        restored.CombinedPrediction!.CapturedAtUtc.Should().Be(latestCapture);
        restarted.AiHttp.Calls.Should().Be(0);
        restarted.Provider.VerifyNoOtherCalls();
        using var scope = restarted.Services.CreateScope();
        var persisted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await persisted.CombinedPredictionSnapshots.AnyAsync(row => row.Id == latestVersion)).Should().BeTrue();
        (await persisted.CombinedPredictionSnapshots.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("ml")]
    [InlineData("ai")]
    [InlineData("narration")]
    public async Task UnavailableSourcesAndNarrationNeverMasqueradeAsComplete(string unavailable)
    {
        using var storage = new TestStorage();
        await using var app = new TestApp(storage.Database) { MissingMl = unavailable == "ml", FailNarration = unavailable == "narration" };
        app.AiHttp.InvalidProbability = unavailable == "ai";
        using var client = await app.StartAsync();
        var job = await RefreshAsync(client, 1, true);
        job.State.Should().Be("completed_with_warnings");
        job.Result!.Warnings.Should().NotBeEmpty();
        job.Result.Prediction.Markets.HasGoals.Should().BeTrue();
        if (unavailable == "narration")
        {
            job.Result.Prediction.Status.Should().Be("complete");
            job.Result.NarrationRefreshed.Should().BeFalse();
            (await ReadMatchAsync(client, 1, "en")).Ai?.Analysis.Should().BeNullOrEmpty();
        }
        else
        {
            job.Result.Prediction.Status.Should().Be("partial");
            var missing = job.Result.Prediction.Sources.Single(source => source.Name == unavailable);
            missing.OutcomeWeight.Should().Be(0);
            missing.GoalsWeight.Should().Be(0);
            missing.UnavailableReason.Should().NotBeNullOrEmpty();
        }
        using var scope = app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CombinedPredictionSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RealAuthorizationAndEligibilityChecksPreventWork()
    {
        using var storage = new TestStorage();
        await using var app = new TestApp(storage.Database);
        using var client = await app.StartAsync();
        using var anonymous = app.CreateClient();
        (await anonymous.PostAsync("/api/automation/predictions/1/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/automation/predictions/jobs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/analyze/1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.DefaultRequestHeaders.Add("X-API-Key", "invalid-key-at-least-sixteen-characters");
        (await anonymous.PostAsync("/api/automation/predictions/1/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/api/automation/predictions/999/refresh", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync("/api/automation/predictions/4/refresh", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/automation/predictions/jobs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.AiHttp.Calls.Should().Be(0);
        app.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BusyRefreshReturnsConflictAndDoesNotStartASecondProviderRequest()
    {
        using var storage = new TestStorage();
        await using var app = new TestApp(storage.Database);
        app.AiHttp.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = await app.StartAsync();
        using var first = await client.PostAsync("/api/automation/predictions/1/refresh?refresh_narration=false", null);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        try
        {
            (await client.PostAsync("/api/automation/predictions/2/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally { app.AiHttp.Release.TrySetResult(); }
        (await PollAsync(client, first.Headers.Location!)).State.Should().Be("completed");
        app.Provider.Verify(service => service.GetPredictionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<CombinedPredictionJob> RefreshAsync(HttpClient client, int fixtureId, bool narration)
    {
        using var accepted = await client.PostAsync($"/api/automation/predictions/{fixtureId}/refresh?refresh_narration={narration.ToString().ToLowerInvariant()}", null);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        accepted.Headers.Location.Should().NotBeNull();
        using var response = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        response.RootElement.GetProperty("data").GetProperty("poll").GetString().Should().Be(accepted.Headers.Location!.ToString());
        return await PollAsync(client, accepted.Headers.Location);
    }

    private static async Task<CombinedPredictionJob> PollAsync(HttpClient client, Uri location)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var envelope = await client.GetFromJsonAsync<ApiResponse<CombinedPredictionJob>>(location, Json, timeout.Token);
            envelope!.Success.Should().BeTrue();
            var job = envelope.Data;
            if (job!.State != "running") return job;
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task<MatchAnalysis> ReadMatchAsync(HttpClient client, int fixtureId, string language)
    {
        using var response = await client.GetAsync($"/api/analyze/{fixtureId}?language={language}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var match = payload.RootElement.GetProperty("data").GetProperty("match");
        match.TryGetProperty("match_prediction", out _).Should().BeTrue();
        foreach (var market in match.GetProperty("presentation").GetProperty("markets").EnumerateArray())
        {
            var checks = market.GetProperty("checks").EnumerateArray().Select(check => check.GetString()!).ToList();
            checks.Count.Should().BeInRange(1, DecisionExplanationPolicy.MaximumChecks);
            checks.Should().OnlyContain(check => !DecisionExplanationPolicy.IsOpaqueCheck(check));
            market.GetProperty("check_outcomes").GetArrayLength().Should().Be(checks.Count);
        }
        return match.Deserialize<MatchAnalysis>(Json)!;
    }

    private static void AssertMarkets(MatchAnalysis match, CombinedPrediction combined)
    {
        match.CombinedPrediction!.CapturedAtUtc.Should().Be(combined.CapturedAtUtc);
        match.MatchPrediction!.Prediction.Should().Be(combined.Markets.ToPrediction().MatchWinner);
        match.Prediction!.HomeWin.Probability.Should().BeApproximately(combined.Markets.HomeWin!.Value, .0001);
        match.Prediction.Draw.Probability.Should().BeApproximately(combined.Markets.Draw!.Value, .0001);
        match.Prediction.AwayWin.Probability.Should().BeApproximately(combined.Markets.AwayWin!.Value, .0001);
        match.Prediction.BTTS.Probability.Should().BeApproximately(combined.Markets.Btts!.Value, .0001);
        match.Prediction.Over25.Probability.Should().BeApproximately(combined.Markets.Over25!.Value, .0001);
        match.Prediction.TwoToThreeGoals.Probability.Should().BeApproximately(combined.Markets.TwoToThreeGoals!.Value, .0001);
    }

    private sealed class TestStorage : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "soccer-e2e-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(_directory, "e2e.db");
        public TestStorage() => Directory.CreateDirectory(_directory);
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestApp(string database, string? postgresConnection = null) : WebApplicationFactory<global::Program>
    {
        public const string AdminKey = "e2e-only-not-a-real-admin-secret";
        public bool MissingMl { get; init; }
        public bool EnableAutomation { get; init; }
        public bool FailNarration { get; init; }
        public Mock<IApiFootballService> Provider { get; } = new(MockBehavior.Strict);
        public List<AiBatchItem> NarrationInputs { get; } = [];
        public AiReply AiHttp { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Provider"] = postgresConnection is null ? "Sqlite" : "Postgres",
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={database};Pooling=False",
                ["ConnectionStrings:PostgresConnection"] = postgresConnection,
                ["AiStartupSync:Enabled"] = "false",
                ["CombinedPredictionAutomation:Enabled"] = EnableAutomation.ToString(),
                ["AiService:Enabled"] = "false",
                ["OpenRouter:ApiKey"] = "sk-or-e2e-not-a-real-provider-key",
                ["AdminApi:ApiKeys:0"] = AdminKey,
                ["Supabase:Url"] = "https://e2e.invalid"
            };
            builder.UseEnvironment("Testing");
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                if (EnableAutomation)
                {
                    var readiness = new Mock<ICombinedPredictionAutomationReadiness>();
                    services.RemoveAll<ICombinedPredictionAutomationReadiness>();
                    services.AddSingleton(readiness.Object);
                }
                var historical = new Mock<IDixonColesModel>();
                historical.Setup(service => service.CalculateProbabilitiesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(Probabilities(1.6, 1.1));
                services.RemoveAll<IDixonColesModel>();
                services.AddSingleton(historical.Object);
                var learned = new Mock<IGoalRateForecaster>();
                learned.Setup(service => service.ForecastAsync(It.IsAny<Fixture>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MissingMl ? null : new GoalRateForecast(1.2, 1.4, Probabilities(1.2, 1.4), "e2e-controlled-ml")
                    { RawMlProbabilities = Probabilities(.8, 1.8) });
                services.RemoveAll<IGoalRateForecaster>();
                services.AddSingleton(learned.Object);
                Provider.Setup(service => service.GetPredictionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ProviderPrediction
                    {
                        PercentHome = .4, PercentDraw = .35, PercentAway = .25,
                        Home = new TeamRecentForm { Played = 5, GoalsForAverage = 1.6, GoalsAgainstAverage = 1.1 },
                        Away = new TeamRecentForm { Played = 5, GoalsForAverage = 1.1, GoalsAgainstAverage = 1.6 }
                    });
                services.RemoveAll<IApiFootballService>();
                services.AddSingleton(Provider.Object);
                services.AddHttpClient(OpenRouterForecastService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => AiHttp);
                var narrator = new Mock<IAiAnalysisService>();
                narrator.Setup(service => service.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
                    .Returns((List<AiBatchItem> items, CancellationToken _) =>
                    {
                        NarrationInputs.AddRange(items);
                        if (FailNarration) throw new InvalidOperationException("Controlled narration failure");
                        return Task.FromResult(items.ToDictionary(item => item.FixtureId, item => new AiBilingualResult
                        {
                            FixtureId = item.FixtureId, Confidence = 65, OverallConfidence = 65,
                            GeneratedAtUtc = DateTimeOffset.UtcNow, ModelVersion = "e2e-controlled-narration",
                            En = new() { Analysis = "English explanation of the freshly combined probabilities." },
                            De = new() { Analysis = "Deutsche Erklärung der frisch kombinierten Wahrscheinlichkeiten." }
                        }));
                    });
                services.RemoveAll<IAiAnalysisService>();
                services.AddSingleton(narrator.Object);
            });
        }

        public async Task<HttpClient> StartAsync()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", AdminKey);
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await context.Fixtures.AnyAsync())
            {
                context.Teams.AddRange(new Team { ApiId = 10, Name = "Test Home", LeagueId = 39 },
                    new Team { ApiId = 20, Name = "Test Away", LeagueId = 39 });
                for (var fixtureId = 1; fixtureId <= 4; fixtureId++)
                    context.Fixtures.Add(new Fixture
                    {
                        Id = fixtureId, ApiId = 100 + fixtureId, LeagueId = 39, HomeTeamId = 10, AwayTeamId = 20,
                        Status = fixtureId == 4 ? "FT" : "NS", Date = DateTimeOffset.UtcNow.AddDays(fixtureId == 4 ? -1 : fixtureId)
                    });
                await context.SaveChangesAsync();
            }
            return client;
        }
    }

    private static PoissonProbabilities Probabilities(double home, double away)
    {
        var markets = DixonColesMath.ComputeMarkets(DixonColesMath.BuildScoreMatrix(home, away, 0, 15));
        return new PoissonProbabilities
        {
            HomeWin = markets.HomeWin, Draw = markets.Draw, AwayWin = markets.AwayWin,
            Over25 = markets.Over25, BothTeamScoredGoal = markets.Btts, TwoToThreeGoals = markets.TwoToThreeGoals,
            BttsAndOver25 = markets.BttsAndOver25, HomeExpectedGoals = home, AwayExpectedGoals = away
        };
    }

    private sealed class AiReply : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool InvalidProbability { get; set; }
        public TaskCompletionSource? Release { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            input.RootElement.GetProperty("model").GetString().Should().EndWith(":free");
            var content = JsonSerializer.Serialize(new
            {
                expected_goals = 2.8, predicted_home_goals = 2, predicted_away_goals = 1,
                home_win_probability = .45, draw_probability = .3, away_win_probability = .25,
                over_2_5_probability = InvalidProbability ? 65 : .65, btts_probability = .6,
                two_to_three_goals_probability = .5, btts_and_over25_probability = .4,
                confidence = .7, rationale = "Controlled E2E response; not a live NVIDIA prediction."
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } })
            };
        }
    }
}
