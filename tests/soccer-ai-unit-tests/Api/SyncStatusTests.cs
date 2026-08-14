using FluentAssertions;
using Mediator.Net.Context;
using Microsoft.EntityFrameworkCore;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Automation;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The endpoint that answers "is the sync actually working". Its whole value is
/// refusing to say "healthy" in the situations where /api/automation/health
/// already does.
/// </summary>
public class SyncStatusTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly GetSyncStatusHandler _sut;

    public SyncStatusTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _sut = new GetSyncStatusHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<GetSyncStatusResponse> RunAsync(GetSyncStatusQuery? query = null)
    {
        var context = new Mock<IReceiveContext<GetSyncStatusQuery>>();
        context.SetupGet(c => c.Message).Returns(query ?? new GetSyncStatusQuery());

        return await _sut.Handle(context.Object, CancellationToken.None);
    }

    private async Task SeedStateAsync(
        DateTimeOffset? lastSuccess, string? lastError = null, string? lastStep = null)
    {
        _db.SyncStates.Add(new SyncState
        {
            Id = SyncState.SingletonId,
            LastSuccessfulSyncUtc = lastSuccess,
            LastRunStartedUtc = lastSuccess,
            LastCompletedStep = lastStep,
            LastError = lastError
        });
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reports_never_run_on_a_fresh_database()
    {
        var result = await RunAsync();

        result.Status.Should().Be(GetSyncStatusResponse.Statuses.NeverRun);
        result.IsStale.Should().BeTrue();
        result.HoursSinceLastSuccess.Should().BeNull();
        result.FixtureCount.Should().Be(0);
    }

    [Fact]
    public async Task Reports_healthy_after_a_recent_clean_run()
    {
        await SeedStateAsync(DateTimeOffset.UtcNow.AddHours(-2), lastStep: "ai_narratives");

        var result = await RunAsync();

        result.Status.Should().Be(GetSyncStatusResponse.Statuses.Healthy);
        result.IsStale.Should().BeFalse();
        result.HoursSinceLastSuccess.Should().BeApproximately(2, 0.1);
        result.LastCompletedStep.Should().Be("ai_narratives");
    }

    [Fact]
    public async Task Reports_stale_when_the_last_success_is_older_than_the_threshold()
    {
        await SeedStateAsync(DateTimeOffset.UtcNow.AddHours(-40));

        var result = await RunAsync();

        result.Status.Should().Be(GetSyncStatusResponse.Statuses.Stale);
        result.IsStale.Should().BeTrue();
    }

    /// <summary>
    /// The production case: the credential broke, so the newest run failed even
    /// though an older one succeeded. Yesterday's success is not today's health.
    /// </summary>
    [Fact]
    public async Task An_unresolved_error_outranks_a_recent_success()
    {
        await SeedStateAsync(
            DateTimeOffset.UtcNow.AddHours(-1),
            lastError: "Credential rejected: API-Football rejected the API key (403).");

        var result = await RunAsync();

        result.Status.Should().Be(GetSyncStatusResponse.Statuses.Failing);
        result.LastError.Should().Contain("403");
    }

    [Fact]
    public async Task Reports_row_counts_as_direct_evidence_of_data()
    {
        await SeedStateAsync(DateTimeOffset.UtcNow.AddHours(-1));

        _db.Fixtures.Add(new Fixture
        {
            Id = 1, ApiId = 1, HomeTeamId = 1, AwayTeamId = 2, LeagueId = 39,
            Date = DateTimeOffset.UtcNow
        });
        _db.Teams.Add(new Team { Id = 1, ApiId = 1, Name = "Arsenal", LeagueId = 39 });
        _db.FixtureAnalyses.Add(new FixtureAnalysis { FixtureId = 1, Lang = "en", SnapshotJson = "{}" });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await RunAsync();

        result.FixtureCount.Should().Be(1);
        result.TeamCount.Should().Be(1);
        result.AnalysisCount.Should().Be(1);
    }

    [Fact]
    public async Task Stale_threshold_is_configurable()
    {
        await SeedStateAsync(DateTimeOffset.UtcNow.AddHours(-5));

        var strict = await RunAsync(new GetSyncStatusQuery { StaleAfterHours = 1 });
        var lenient = await RunAsync(new GetSyncStatusQuery { StaleAfterHours = 48 });

        strict.Status.Should().Be(GetSyncStatusResponse.Statuses.Stale);
        lenient.Status.Should().Be(GetSyncStatusResponse.Statuses.Healthy);
    }
}
