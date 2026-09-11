using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Decisions;
using SoccerAi.Application.Services.Statistics;
using SoccerAi.Infrastructure.Persistence;

namespace soccer_ai_unit_tests.Services;

public sealed class PredictionIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite("Data Source=:memory:")
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)).Options);
    private static Fixture Match(int id = 1) => new() { Id = id, ApiId = id, HomeTeamId = 10, AwayTeamId = 20, Status = "NS", Date = Now.AddHours(6), LeagueId = 39,
        OddsCheckedAtUtc = Now, OddsUpdatedAtUtc = Now.AddMinutes(-10), BttsYesOdds = 1.9 };
    // bttsProb is a parameter because WeightedPrediction is init-only: the NaN
    // case below has to be built, not mutated after construction.
    private static WeightedPrediction Prediction(double bttsProb = .7) => new() {
        HomeProb = .5, DrawProb = .25, AwayProb = .25,
        MatchWinner = "home", Over25Prob = .4, Over25 = false, BTTSProb = bttsProb, BTTS = true,
        TwoToThreeGoalsProb = .6, TwoToThreeGoals = true };
    private static DecisionAudit Audit(bool qualified = true) => new(2, [
        new(ConfluenceRuleEngine.Markets.Btts, .7, .6, true, 3, 0, qualified, []) {
            Odds = 2, MinOdds = 1.7, Ev = .4, MinEdge = .05, KellyStake = .1, KellyFraction = .25,
            ComboEligible = qualified, GateOutcome = "qualified" }
    ], Now.AddDays(-2));

    [Fact]
    public void PriceDropWithdrawsOldHighValuePickAndComboLeg()
    {
        var fixture = Match(); fixture.BttsYesOdds = 1.69;
        var repriced = LiveOddsPolicy.Reprice(Audit(), fixture, Now).Markets.Single();
        repriced.Odds.Should().Be(1.69);
        repriced.Qualified.Should().BeFalse(); repriced.ComboEligible.Should().BeFalse();
        repriced.KellyStake.Should().BeNull();
    }

    [Fact]
    public void AcceptedRepriceRecomputesKellyAndDoesNotPromoteRejectedConfluence()
    {
        var result = LiveOddsPolicy.Reprice(Audit(), Match(), Now).Markets.Single();
        result.Ev.Should().BeApproximately(.33, .0001);
        result.KellyStake.Should().Be(ValueMath.FractionalKelly(.7, 1.9, .25));
        result.KellyStake.Should().NotBe(.1);
        LiveOddsPolicy.Reprice(Audit(false), Match(), Now).Markets.Single().Qualified.Should().BeFalse();
    }

    [Theory]
    [InlineData(-181, 0, "NS", 6)]
    [InlineData(-1, 1, "NS", 6)]
    [InlineData(-1, 0, "FT", 6)]
    [InlineData(-1, 0, "NS", 0)]
    public void StaleFutureOrStartedPricesAreNotLive(int providerMinutes, int checkedMinutes, string status, int kickoffHours)
    {
        var fixture = Match(); fixture.OddsUpdatedAtUtc = Now.AddMinutes(providerMinutes);
        fixture.OddsCheckedAtUtc = Now.AddMinutes(checkedMinutes); fixture.Status = status;
        fixture.Date = Now.AddHours(kickoffHours);
        LiveOddsPolicy.IsFresh(fixture, Now).Should().BeFalse();
    }

    [Fact]
    public void MissingAndOldMarketsDoNotInheritOtherMarketsFreshness()
    {
        var fixture = Match(); fixture.HomeWinOdds = 2.5;
        FixtureOddsWriter.ReplaceLivePrices(fixture, [new OddsQuote("Book", "btts", 1.9, Now.AddHours(-4))], Now);
        fixture.BttsYesOdds.Should().BeNull(); fixture.HomeWinOdds.Should().BeNull();
        LiveOddsPolicy.IsFresh(fixture, Now).Should().BeFalse();
    }

    [Fact]
    public async Task LedgerCapturesOnceAndRejectsFinishedOrInvalidProbabilities()
    {
        await using var db = Database(); await db.Database.OpenConnectionAsync(); await db.Database.MigrateAsync();
        db.Teams.AddRange(new Team { ApiId = 10 }, new Team { ApiId = 20 }); await db.SaveChangesAsync();
        var fixture = Match(); db.Fixtures.Add(fixture); await db.SaveChangesAsync();
        var ledger = new PredictionLedger(db, new Clock());
        await ledger.RecordAsync(fixture, Prediction(), Prediction(), "v1", "{}");
        await ledger.RecordAsync(fixture, Prediction(), Prediction(), "v2", "{}");
        (await db.PredictionSnapshots.SingleAsync()).ModelVersion.Should().Be("v1");
        fixture.Status = "FT";
        await ledger.RecordAsync(fixture, Prediction(), Prediction(), "v3", "{}");
        var other = Match(2); var invalid = Prediction(bttsProb: double.NaN);
        await ledger.RecordAsync(other, invalid, Prediction(), "v4", "{}");
        (await db.PredictionSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task StatisticsScoresRecordedPicksExcludesRecomputedAndRescheduledForecasts()
    {
        await using var db = Database(); await db.Database.OpenConnectionAsync(); await db.Database.MigrateAsync();
        db.Teams.AddRange(new Team { ApiId = 10 }, new Team { ApiId = 20 }); await db.SaveChangesAsync();
        var kickoff = Now.AddDays(-2);
        db.Fixtures.AddRange(new Fixture { Id = 1, ApiId = 1, HomeTeamId = 10, AwayTeamId = 20, Date = kickoff, Status = "FT", LeagueId = 39, HomeGoal = 3, AwayGoal = 0 },
            new Fixture { Id = 2, ApiId = 2, HomeTeamId = 10, AwayTeamId = 20, Date = kickoff, Status = "FT", LeagueId = 39, HomeGoal = 0, AwayGoal = 0 });
        PredictionSnapshot Row(DateTimeOffset capture, long window, bool gg = true) => new() {
            FixtureId = 1, KickoffUtc = kickoff, CapturedAtUtc = capture, CaptureWindow = window, ModelVersion = "v1",
            Home = .5, Draw = .25, Away = .25, Winner = "home", Btts = .7, BttsPick = gg,
            Over25 = .4, Over25Pick = false, Goals23 = .6, Goals23Pick = true };
        db.PredictionSnapshots.AddRange(Row(kickoff.AddHours(-4), 1), Row(kickoff.AddHours(-2), 2),
            Row(kickoff.AddMinutes(-30), 3, false), Row(kickoff.AddHours(2), 4, false));
        var rescheduled = Row(kickoff.AddDays(-1), 5); rescheduled.FixtureId = 2; rescheduled.KickoffUtc = kickoff.AddDays(1);
        db.PredictionSnapshots.Add(rescheduled); await db.SaveChangesAsync();
        var stats = await new PredictionStatisticsService(db, new Clock()).GetAsync(30);
        stats.ScoredMatches.Should().Be(1); stats.MissingPreMatchPrediction.Should().Be(1);
        stats.Errors.BttsPickedButNoBttsOver25.Should().Be(1);
        stats.Errors.Under25PickedButOver25.Should().Be(1);
        stats.Markets.Single(m => m.Market == "btts").FalsePositives.Should().Be(1);
        stats.Markets.Single(m => m.Market == "over25").FalseNegatives.Should().Be(1);
        stats.Markets.Single(m => m.Market == "1x2").Accuracy.Should().Be(1);
        stats.Leagues.Single().Matches.Should().Be(1);
    }

    [Fact]
    public void EmptyStatsAreUnknownAndPerfectTinySamplesHaveWideIntervals()
    {
        PredictionStatisticsService.Metrics([]).Should().OnlyContain(m => m.Accuracy == null && m.Lower95 == null);
        PredictionStatisticsService.Wilson(4, 4).Lower.Should().BeLessThan(.52);
    }
}
