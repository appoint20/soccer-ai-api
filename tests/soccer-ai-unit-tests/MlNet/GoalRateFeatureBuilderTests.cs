using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet;
using SoccerAi.Infrastructure.MlNet.Models;

namespace soccer_ai_unit_tests.MlNet;

public class GoalRateFeatureBuilderTests
{
    private static GoalRateFeatureBuilder Builder() =>
        new(Options.Create(new DixonColesOptions()), NullLogger<GoalRateFeatureBuilder>.Instance);

    private static Fixture Fx(
        int id, int home, int away, DateTimeOffset date,
        int homeGoal = 0, int awayGoal = 0, string status = "FT",
        double xgHome = 1.2, double xgAway = 1.0, int league = 39) =>
        new()
        {
            Id = id,
            HomeTeamId = home,
            AwayTeamId = away,
            LeagueId = league,
            Date = date,
            Status = status,
            HomeGoal = homeGoal,
            AwayGoal = awayGoal,
            HomeXg = xgHome,
            AwayXg = xgAway,
            HomeShots = 12,
            AwayShots = 9,
            HomeShotsOnTarget = 5,
            AwayShotsOnTarget = 3,
        };

    /// <summary>A season of alternating fixtures between four teams.</summary>
    private static List<Fixture> Season(DateTimeOffset start, int count, int goalsPerSide = 1)
    {
        var teams = new[] { 10, 11, 12, 13 };
        var list = new List<Fixture>();
        for (var i = 0; i < count; i++)
        {
            var h = teams[i % teams.Length];
            var a = teams[(i / teams.Length + 1) % teams.Length];
            if (h == a) a = teams[(i + 2) % teams.Length];
            list.Add(Fx(i + 1, h, a, start.AddDays(i * 3), goalsPerSide, goalsPerSide));
        }

        return list;
    }

    // ── Anti-leakage: the property everything else depends on ───────────────

    [Fact]
    public void FirstFixture_HasNoHistoryFeatures()
    {
        var rows = Builder().Build([Fx(1, 10, 11, DateTimeOffset.UtcNow.AddDays(-30), 3, 2)]);

        var row = rows.Should().ContainSingle().Subject;
        // Nothing preceded it, so every rolling window must be empty — a 3-2
        // result leaking into its own features would show up here.
        row.HomeGoalsFor.Should().Be(0);
        row.AwayGoalsFor.Should().Be(0);
        row.HomeXgFor.Should().Be(0);
        row.HomeMatchTotal.Should().Be(0);
        row.HomeHistory.Should().Be(0);
        row.AwayHistory.Should().Be(0);
    }

    [Fact]
    public void RowFeatures_ExcludeTheFixturesOwnResult()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = new List<Fixture>
        {
            Fx(1, 10, 11, start, 1, 1),
            // A 5-0 blowout. If the builder leaked, the row for THIS fixture
            // would already show the inflated scoring rate it produces.
            Fx(2, 10, 12, start.AddDays(7), 5, 0),
            Fx(3, 10, 13, start.AddDays(14), 0, 0),
        };

        var rows = Builder().Build(fixtures);

        var second = rows.Single(r => r.FixtureId == 2);
        // Team 10's only prior match was the 1-1, so its rolling "goals for"
        // must be exactly 1 — not 3 (the average including the 5-0).
        second.HomeGoalsFor.Should().Be(1);

        var third = rows.Single(r => r.FixtureId == 3);
        // Now the 5-0 counts: (1 + 5) / 2 = 3.
        third.HomeGoalsFor.Should().Be(3);
    }

    [Fact]
    public void HistoryCounts_IncreaseOnlyAfterAFixtureIsPlayed()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = new List<Fixture>
        {
            Fx(1, 10, 11, start, 1, 0),
            Fx(2, 10, 11, start.AddDays(7), 2, 1),
            Fx(3, 10, 11, start.AddDays(14), 0, 3),
        };

        var rows = Builder().Build(fixtures).OrderBy(r => r.FixtureId).ToList();

        rows[0].HomeHistory.Should().Be(0);
        rows[1].HomeHistory.Should().Be(1);
        rows[2].HomeHistory.Should().Be(2);
    }

    // ── Upcoming fixtures ───────────────────────────────────────────────────

    [Fact]
    public void UnplayedFixture_IsNotLabelledAndDoesNotUpdateHistory()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = new List<Fixture>
        {
            Fx(1, 10, 11, start, 2, 1),
            // Scheduled, not played. HomeGoal/AwayGoal are 0 by default and must
            // not be read as a real 0-0.
            Fx(2, 10, 11, start.AddDays(7), status: "NS"),
            Fx(3, 10, 11, start.AddDays(14), 1, 1),
        };

        var rows = Builder().Build(fixtures).OrderBy(r => r.FixtureId).ToList();

        rows[1].IsFinished.Should().BeFalse();
        rows[1].GoalsHome.Should().Be(0);
        rows[1].GoalsAway.Should().Be(0);

        // Fixture 3 must see only fixture 1, because 2 was never played.
        rows[2].HomeHistory.Should().Be(1);
        rows[2].HomeGoalsFor.Should().Be(2);
    }

    [Fact]
    public void UpcomingFixture_StillReceivesFeaturesFromPriorHistory()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = Season(start, 24);
        fixtures.Add(Fx(999, 10, 11, start.AddDays(200), status: "NS"));

        var rows = Builder().Build(fixtures);

        // This is what live prediction depends on: an unplayed fixture is
        // featurised from the history before it, exactly like a training row.
        var upcoming = rows.Single(r => r.FixtureId == 999);
        upcoming.IsFinished.Should().BeFalse();
        upcoming.HomeHistory.Should().BeGreaterThan(0);
        upcoming.HomeGoalsFor.Should().BeGreaterThan(0);
    }

    // ── Dixon-Coles block ───────────────────────────────────────────────────

    [Fact]
    public void DixonColes_StaysZeroUntilThereIsEnoughHistory()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var rows = Builder().Build(Season(start, 6));

        // Default MinLeagueMatches is 10, so nothing early can be priced. The
        // block staying at zero is what tells the trainer to lean on the market
        // and form features instead.
        rows.Take(4).Should().OnlyContain(r => r.DcLambdaSum == 0);
    }

    [Fact]
    public void DixonColes_ProducesCoherentProbabilitiesOnceWarmedUp()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var rows = Builder().Build(Season(start, 60));

        var priced = rows.Where(r => r.DcLambdaSum > 0).ToList();
        priced.Should().NotBeEmpty();

        foreach (var r in priced)
        {
            r.DcOver25.Should().BeInRange(0, 1);
            r.DcBtts.Should().BeInRange(0, 1);
            // Every market comes off one renormalised matrix, so 1X2 must sum to 1.
            (r.DcHome + r.DcDraw + r.DcAway).Should().BeApproximately(1f, 1e-4f);
        }
    }

    // ── Market features ─────────────────────────────────────────────────────

    /// <summary>
    /// Prices alone are not enough — they must be timestamped as pre-match.
    /// </summary>
    [Fact]
    public void MarketLambda_IsPopulatedWhenTimestampedPricesExist()
    {
        var kickoff = DateTimeOffset.UtcNow.AddHours(6);
        var f = Fx(1, 10, 11, kickoff, 1, 1);
        f.HomeWinOdds = 2.10;
        f.DrawOdds = 3.40;
        f.AwayWinOdds = 3.60;
        f.Over25Odds = 1.90;
        f.Under25Odds = 1.95;
        // Provenance: fetched before kickoff, and the prices are no older than
        // the fetch that observed them.
        f.OddsUpdatedAtUtc = kickoff.AddHours(-8);
        f.OddsCheckedAtUtc = kickoff.AddHours(-7);

        var row = Builder().Build([f]).Single();

        row.HasMktLambda.Should().Be(1f);
        row.MktBttsImplied.Should().BeInRange(0.01f, 0.99f);
        row.MktLambdaSum.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Untimestamped prices are dropped, not used.
    /// </summary>
    /// <remarks>
    /// This is the training-time leakage guard. Historical odds imported in bulk
    /// carry no capture time, so nothing proves they were obtainable before the
    /// match — a closing price folded into a feature is the model reading the
    /// result. The whole market block stays zero rather than being trusted.
    /// </remarks>
    [Fact]
    public void MarketFeatures_AreDroppedWhenPricesHaveNoProvenance()
    {
        var f = Fx(1, 10, 11, DateTimeOffset.UtcNow.AddHours(6), 1, 1);
        f.HomeWinOdds = 2.10;
        f.DrawOdds = 3.40;
        f.AwayWinOdds = 3.60;
        f.Over25Odds = 1.90;
        f.Under25Odds = 1.95;
        // No OddsCheckedAtUtc / OddsUpdatedAtUtc.

        var row = Builder().Build([f]).Single();

        row.HasMktLambda.Should().Be(0f);
        row.HasMktOver.Should().Be(0f);
        row.MktLambdaSum.Should().Be(0f);
    }

    [Fact]
    public void MarketFeatures_StayZeroWithNoPrices()
    {
        var row = Builder()
            .Build([Fx(1, 10, 11, new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), 1, 1)])
            .Single();

        row.HasMktLambda.Should().Be(0f);
        row.HasMktOver.Should().Be(0f);
        row.HasMktBtts.Should().Be(0f);
        // A divergence against an absent price is not a disagreement.
        row.DivOver.Should().Be(0f);
        row.DivBtts.Should().Be(0f);
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [Fact]
    public void Build_EmitsOneRowPerFixtureInKickoffOrder()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = Season(start, 12);
        // Deliberately shuffled: ordering is the builder's responsibility.
        fixtures.Reverse();

        var rows = Builder().Build(fixtures);

        rows.Should().HaveCount(12);
        rows.Select(r => r.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public void FeatureColumns_ExcludeIdentifiersAndLabels()
    {
        var columns = SoccerAi.Infrastructure.MlNet.Models.GoalRateRow.FeatureColumns();

        columns.Should().NotContain("FixtureId");
        columns.Should().NotContain("GoalsHome");
        columns.Should().NotContain("GoalsAway");
        columns.Should().NotContain("LeagueId");
        columns.Should().Contain("DcOver25");
        columns.Should().Contain("MktBttsImplied");
    }

    [Fact]
    public void SameDayAndStoredFutureEloCannotChangeEarlierForecastFeatures()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = Season(start, 40);
        var target = Fx(100, 10, 11, start.AddDays(130), status: "NS");
        var sameDay = Fx(101, 10, 12, target.Date.AddHours(-2), 0, 0);
        fixtures.Add(target); fixtures.Add(sameDay);
        var before = Builder().Build(fixtures).Single(r => r.FixtureId == 100);
        sameDay.HomeGoal = 10; sameDay.AwayGoal = 9;
        foreach (var f in fixtures) { f.HomeElo = 9999; f.AwayElo = 10; }
        var after = Builder().Build(fixtures).Single(r => r.FixtureId == 100);
        foreach (var column in GoalRateRow.FeatureColumns())
            typeof(GoalRateRow).GetProperty(column)!.GetValue(after).Should()
                .Be(typeof(GoalRateRow).GetProperty(column)!.GetValue(before), column);
    }

    [Fact]
    public void CalibrationSplitKeepsEntireDaysOutOfTraining()
    {
        var rows = Enumerable.Range(0, 40).Select(i => new GoalRateRow {
            FixtureId = i, Date = new DateTime(2026, 1, 1).AddDays(i / 4).AddHours(i % 4)
        }).ToList();
        var (fit, calibration) = GoalRateTrainingService.SplitCalibration(rows, .15);
        fit.Max(r => r.Date.Date).Should().BeBefore(calibration.Min(r => r.Date.Date));
        fit.Select(r => r.FixtureId).Intersect(calibration.Select(r => r.FixtureId)).Should().BeEmpty();
        (fit.Count + calibration.Count).Should().Be(rows.Count);
    }

    [Fact]
    public void ArtifactRejectsFeatureDriftAndDatesUsedForCalibration()
    {
        var dc = new DixonColesOptions(); var hybrid = new HybridModelOptions();
        var artifact = new GoalRateArtifact { PredictionRecipe = GoalRateEnsemble.Recipe, Features = GoalRateRow.FeatureColumns(), DixonColes = dc,
            LambdaMin = hybrid.LambdaMin, LambdaMax = hybrid.LambdaMax,
            TrainingThroughUtc = new DateTime(2026, 1, 1), CalibrationFromUtc = new DateTime(2026, 1, 2),
            CalibrationThroughUtc = new DateTime(2026, 1, 9) };
        artifact.Supports(dc, hybrid).Should().BeTrue();
        (artifact with { PredictionRecipe = "" }).Supports(dc, hybrid).Should().BeTrue();
        (artifact with { PredictionRecipe = "unknown" }).Supports(dc, hybrid).Should().BeFalse();
        artifact.CanScore(new DateTime(2026, 1, 9, 22, 0, 0)).Should().BeFalse();
        artifact.CanScore(new DateTime(2026, 1, 10)).Should().BeTrue();
        (artifact with { Features = artifact.Features.Reverse().ToArray() }).Supports(dc, hybrid).Should().BeFalse();
    }
}
