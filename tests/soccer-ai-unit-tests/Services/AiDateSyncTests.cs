using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Application.Services.Sync;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The manual "narrate this date" run. It must target exactly the day the app
/// lists, never narrate a match that has started, and account for every
/// fixture it did not narrate rather than silently leaving it out.
/// </summary>
public class AiDateSyncTests
{
    private const int CoveredLeague = 39;
    private const int OtherLeague = 999;

    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private static Fixture Match(int id, DateTimeOffset kickoff, int league = CoveredLeague, string status = "NS") =>
        new() { Id = id, Date = kickoff, Status = status, LeagueId = league, HomeTeamId = id * 10, AwayTeamId = id * 10 + 1 };

    private static AiBilingualResult Valid(int fixtureId, string text = "Fresh analysis.") => new()
    {
        FixtureId = fixtureId, Confidence = 70, OverallConfidence = 70,
        En = new AiLanguageBlock { Analysis = text },
        De = new AiLanguageBlock { Analysis = text + " (de)" },
    };

    private static (ApplicationDbContext Db, Mock<IAiAnalysisService> Provider, Mock<IAnalysisPrecomputeService> Precompute, AiSyncService Sut)
        Build(IEnumerable<Fixture> fixtures, IEnumerable<FixtureAnalysis>? existing = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Fixtures.AddRange(fixtures);
        if (existing is not null) db.FixtureAnalyses.AddRange(existing);
        db.SaveChanges();

        var analysis = new Mock<IMatchAnalysisService>();
        analysis.Setup(x => x.AnalyzeFixtureAsync(It.IsAny<Fixture>(), "en", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Fixture f, string lang, bool refresh, CancellationToken ct) => new FixtureAnalysisResult
            {
                FixtureId = f.Id, TeamStats = new TeamStatsResponse(), Models = new StatisticalModels(),
                H2H = new HeadToHeadModel(), Decisions = new DecisionServiceResult(), LeagueName = "League",
                Prediction = new WeightedPrediction { HomeProb = .5, Over25Prob = .6 },
            });

        var provider = new Mock<IAiAnalysisService>();
        provider.Setup(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<AiBatchItem> items, CancellationToken ct) => items.ToDictionary(i => i.FixtureId, i => Valid(i.FixtureId)));

        var tiers = new Mock<ILeagueTierService>();
        tiers.Setup(t => t.GetSyncLeagueIds()).Returns(new[] { CoveredLeague });

        var precompute = new Mock<IAnalysisPrecomputeService>();
        var sut = new AiSyncService(db, analysis.Object, provider.Object, precompute.Object, tiers.Object,
            NullLogger<AiSyncService>.Instance);
        return (db, provider, precompute, sut);
    }

    private static FixtureAnalysis Narrated(int fixtureId, string lang, string text = "Existing analysis.") =>
        new() { FixtureId = fixtureId, Lang = lang, Confidence = 60, Analysis = text };

    [Fact]
    public async Task EveryFixtureOnTheDateIsAccountedFor()
    {
        var (db, provider, precompute, sut) = Build(
            [
                Match(1, At(Tomorrow, 12)),                          // narrated
                Match(2, At(Tomorrow, 15)),                          // already has text
                Match(3, At(Tomorrow, 12), league: OtherLeague),     // not a synced league
                Match(4, At(Tomorrow, 18), status: "PST"),           // postponed
                Match(5, At(Tomorrow.AddDays(1), 12)),               // a different date
            ],
            [Narrated(2, "en"), Narrated(2, "de")]);

        var report = await sut.SyncDateAsync(Tomorrow);

        report.FixturesOnDate.Should().Be(4);
        report.OutOfScope.Should().Be(1);
        report.NotUpcoming.Should().Be(1);
        report.Candidates.Should().Be(2);
        report.AlreadyAnalyzed.Should().Be(1);
        report.Attempted.Should().Be(1);
        report.Generated.Should().Be(1);
        report.Failed.Should().Be(0);

        // The partition the report promises: nothing on the date goes unexplained.
        (report.OutOfScope + report.NotUpcoming + report.Candidates).Should().Be(report.FixturesOnDate);
        (report.AlreadyAnalyzed + report.Failed + report.Generated).Should().Be(report.Candidates);

        (await db.FixtureAnalyses.Where(a => a.FixtureId == 1).ToListAsync())
            .Should().HaveCount(2).And.OnlyContain(a => a.Analysis.Length > 0);
        precompute.Verify(x => x.RecomputeFixtureAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(x => x.AnalyzeBatchAsync(
            It.Is<List<AiBatchItem>>(items => items.Any(i => i.FixtureId != 1)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForceRegeneratesTextThatAlreadyExists()
    {
        var (db, _, _, sut) = Build([Match(1, At(Tomorrow, 12))], [Narrated(1, "en"), Narrated(1, "de")]);

        var report = await sut.SyncDateAsync(Tomorrow, force: true);

        report.AlreadyAnalyzed.Should().Be(0);
        report.Generated.Should().Be(1);
        (await db.FixtureAnalyses.SingleAsync(a => a.FixtureId == 1 && a.Lang == "en"))
            .Analysis.Should().Be("Fresh analysis.");
    }

    /// <summary>
    /// A manual run reports what to retry. Throwing would discard the counts
    /// for every fixture that did succeed.
    /// </summary>
    [Fact]
    public async Task AFixtureTheModelFailsIsReportedNotThrown()
    {
        var (_, provider, _, sut) = Build([Match(7, At(Tomorrow, 12))]);
        provider.Setup(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, AiBilingualResult>());

        var report = await sut.SyncDateAsync(Tomorrow);

        report.Attempted.Should().Be(1);
        report.Generated.Should().Be(0);
        report.Failed.Should().Be(1);
        report.FailedFixtureIds.Should().Equal(7);
    }

    /// <summary>A rejected key fails every fixture the same way, so it stops the run.</summary>
    [Fact]
    public async Task ARejectedCredentialStopsTheRun()
    {
        var (_, provider, _, sut) = Build([Match(1, At(Tomorrow, 12)), Match(2, At(Tomorrow, 13))]);
        provider.Setup(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalApiException("OpenRouter", "Key rejected", System.Net.HttpStatusCode.Unauthorized));

        var run = () => sut.SyncDateAsync(Tomorrow);

        (await run.Should().ThrowAsync<ExternalApiException>())
            .Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        provider.Verify(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Status lags kickoff: a live match can still read "NS". The window, not
    /// the status, is what keeps a started match from being narrated.
    /// </summary>
    [Fact]
    public async Task AMatchThatHasKickedOffIsNeverNarrated()
    {
        var kickoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (_, provider, _, sut) = Build([Match(1, kickoff)]);

        var report = await sut.SyncDateAsync(DateOnly.FromDateTime(kickoff.UtcDateTime));

        report.NotUpcoming.Should().Be(1);
        report.Candidates.Should().Be(0);
        provider.Verify(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The scheduled pipeline depends on this throwing: it is how an
    /// incomplete narrative step reaches LastError instead of reading as success.
    /// </summary>
    [Fact]
    public async Task TheScheduledSyncStillThrowsWhenFixturesAreLeftWithoutText()
    {
        var (_, provider, _, sut) = Build([Match(1, At(Tomorrow, 12))]);
        provider.Setup(x => x.AnalyzeBatchAsync(It.IsAny<List<AiBatchItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, AiBilingualResult>());

        var run = () => sut.SyncUpcomingFixturesAsync(DateTime.UtcNow, daysAhead: 5);

        (await run.Should().ThrowAsync<ExternalApiException>())
            .Which.Message.Should().Contain("AI analysis incomplete: 0 persisted, 1 failed");
    }
}
