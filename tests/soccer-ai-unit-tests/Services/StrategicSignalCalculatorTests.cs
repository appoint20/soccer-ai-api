using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Signals;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Synthetic-history tests for the strategic signal catalog. Home team = 100,
/// away team = 200. Histories are newest-first, as the service delivers them.
/// </summary>
public class StrategicSignalCalculatorTests
{
    private const int HomeId = 100;
    private const int AwayId = 200;
    private static readonly DateTimeOffset Kickoff = new(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly StrategyOptions Opt = new();

    // ── Synthetic history builders ───────────────────────────────────────────

    /// <summary>Match from the perspective of a team: goals for/against, at home or away.</summary>
    private static Fixture Match(int teamId, bool atHome, int gf, int ga, int daysAgo,
        int? htFor = null, int? htAgainst = null, int leagueId = 39) => new()
    {
        Id = Random.Shared.Next(100000, 999999),
        LeagueId = leagueId,
        Status = "FT",
        Date = Kickoff.AddDays(-daysAgo),
        HomeTeamId = atHome ? teamId : 999,
        AwayTeamId = atHome ? 999 : teamId,
        HomeGoal = atHome ? gf : ga,
        AwayGoal = atHome ? ga : gf,
        HtHomeGoal = atHome ? htFor ?? gf / 2 : htAgainst ?? ga / 2,
        HtAwayGoal = atHome ? htAgainst ?? ga / 2 : htFor ?? gf / 2
    };

    private static Fixture H2HMatch(int homeId, int awayId, int hg, int ag, int daysAgo) => new()
    {
        Id = Random.Shared.Next(100000, 999999),
        LeagueId = 39,
        Status = "FT",
        Date = Kickoff.AddDays(-daysAgo),
        HomeTeamId = homeId,
        AwayTeamId = awayId,
        HomeGoal = hg,
        AwayGoal = ag
    };

    private static Fixture TheFixture(double? homeOdds = null, double? drawOdds = null,
        double? awayOdds = null, double? over = null, double? under = null,
        double? btts = null, bool derby = false) => new()
    {
        Id = 1,
        LeagueId = 39,
        Status = "NS",
        Date = Kickoff,
        HomeTeamId = HomeId,
        AwayTeamId = AwayId,
        HomeWinOdds = homeOdds,
        DrawOdds = drawOdds,
        AwayWinOdds = awayOdds,
        Over25Odds = over,
        Under25Odds = under,
        BttsYesOdds = btts,
        IsDerby = derby
    };

    private static SignalInputs Inputs(
        List<Fixture>? homeHistory = null, List<Fixture>? awayHistory = null,
        List<Fixture>? h2h = null, List<Fixture>? league = null,
        Team? homeTeam = null, Team? awayTeam = null,
        Fixture? fixture = null, PoissonModel? dc = null,
        bool homeTier2 = false, bool awayTier2 = false, double volatility = 0.08) =>
        new(fixture ?? TheFixture(), homeTeam, awayTeam,
            homeHistory ?? [], awayHistory ?? [], h2h ?? [], league ?? [],
            homeTier2, awayTier2, dc, volatility);

    private static Team MakeTeam(int apiId, int rank, int points, int played, int gd = 0) => new()
    {
        ApiId = apiId, Name = $"Team{apiId}", Rank = rank, Points = points, Played = played, GoalsDiff = gd
    };

    // ── A. Scoring & conceding ───────────────────────────────────────────────

    [Fact]
    public void ScoredInLast3Venue_AllScored_FlagOn()
    {
        var history = new List<Fixture>
        {
            Match(HomeId, atHome: true, gf: 2, ga: 0, daysAgo: 3),
            Match(HomeId, atHome: false, gf: 0, ga: 1, daysAgo: 7),  // away match — ignored for venue
            Match(HomeId, atHome: true, gf: 1, ga: 1, daysAgo: 10),
            Match(HomeId, atHome: true, gf: 3, ga: 2, daysAgo: 17),
            Match(HomeId, atHome: true, gf: 0, ga: 0, daysAgo: 24)
        };

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.ScoredInLast3Venue.Value.Should().Be(3);
        s.HomeScoring.ScoredInLast3Venue.Flag.Should().BeTrue("scored in all of the last 3 home matches");
    }

    [Fact]
    public void ConcededInLast3_CleanDefense_FlagOff()
    {
        var history = Enumerable.Range(1, 5)
            .Select(i => Match(HomeId, atHome: true, gf: 1, ga: 0, daysAgo: i * 7)).ToList();

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.ConcededInLast3Venue.Value.Should().Be(0);
        s.HomeScoring.ConcededInLast3Venue.Flag.Should().BeFalse();
    }

    [Fact]
    public void FailedToScore_And_CleanSheets_CountsAndFlags()
    {
        var history = new List<Fixture>
        {
            Match(AwayId, atHome: false, gf: 0, ga: 2, daysAgo: 3),   // FTS
            Match(AwayId, atHome: false, gf: 0, ga: 1, daysAgo: 10),  // FTS
            Match(AwayId, atHome: false, gf: 2, ga: 0, daysAgo: 17),  // clean sheet
            Match(AwayId, atHome: false, gf: 1, ga: 0, daysAgo: 24),  // clean sheet
            Match(AwayId, atHome: false, gf: 1, ga: 1, daysAgo: 31)
        };

        var s = StrategicSignalCalculator.Compute(Inputs(awayHistory: history), Opt);

        s.AwayScoring.FailedToScoreLast5Venue.Value.Should().Be(2);
        s.AwayScoring.FailedToScoreLast5Venue.Flag.Should().BeTrue();
        s.AwayScoring.CleanSheetsLast5Venue.Value.Should().Be(2);
        s.AwayScoring.CleanSheetsLast5Venue.Flag.Should().BeTrue();
    }

    [Fact]
    public void AttackTrend_ScoringMoreRecently_PositiveAndFlagged()
    {
        // Season avg ~1.0, last-5 home avg 2.0 → trend +1.0
        var recent = Enumerable.Range(1, 5)
            .Select(i => Match(HomeId, atHome: true, gf: 2, ga: 0, daysAgo: i * 7)).ToList();
        var old = Enumerable.Range(6, 10)
            .Select(i => Match(HomeId, atHome: true, gf: 0, ga: 0, daysAgo: i * 7)).ToList();

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: [.. recent, .. old]), Opt);

        s.HomeScoring.AttackTrend.Value.Should().BeGreaterThan(0.3);
        s.HomeScoring.AttackTrend.Flag.Should().BeTrue();
    }

    [Fact]
    public void Over25AndBttsRates_ComputedVenueSplit()
    {
        var history = new List<Fixture>
        {
            Match(HomeId, true, 3, 1, 3),   // O2.5 + BTTS
            Match(HomeId, true, 2, 1, 10),  // O2.5 + BTTS
            Match(HomeId, true, 1, 0, 17),  // neither
            Match(HomeId, true, 2, 2, 24),  // O2.5 + BTTS
            Match(HomeId, true, 0, 0, 31)   // neither
        };

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.Over25RateLast5Venue.Value.Should().BeApproximately(0.6, 1e-9);
        s.HomeScoring.Over25RateLast5Venue.Flag.Should().BeTrue();
        s.HomeScoring.BttsRateLast5Venue.Value.Should().BeApproximately(0.6, 1e-9);
        s.HomeScoring.Under25RateLast5Venue.Value.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void SecondHalfGoalShare_LateTeam_Flagged()
    {
        // All goals after half-time: HT 0, FT 2 in every match
        var history = Enumerable.Range(1, 10)
            .Select(i => Match(HomeId, true, gf: 2, ga: 0, daysAgo: i * 7, htFor: 0, htAgainst: 0))
            .ToList();

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.SecondHalfGoalShare.Value.Should().Be(1.0);
        s.HomeScoring.SecondHalfGoalShare.Flag.Should().BeTrue("100% of goals come after the break");
        s.HomeScoring.FirstHalfGoalShare.Value.Should().Be(0.0);
    }

    [Fact]
    public void AvgTotalGoals_ChaosTeam_Flagged()
    {
        var history = Enumerable.Range(1, 5)
            .Select(i => Match(HomeId, true, gf: 3, ga: 2, daysAgo: i * 7)).ToList();

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.AvgTotalGoalsLast5.Value.Should().Be(5.0);
        s.HomeScoring.AvgTotalGoalsLast5.Flag.Should().BeTrue();
    }

    [Fact]
    public void ScoringDrought_CurrentStreakOnly()
    {
        var history = new List<Fixture>
        {
            Match(HomeId, true, 0, 1, 3),    // no goal (newest)
            Match(HomeId, false, 0, 2, 10),  // no goal
            Match(HomeId, true, 2, 0, 17),   // scored → streak stops here
            Match(HomeId, true, 0, 0, 24)
        };

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeScoring.ScoringDrought.Value.Should().Be(2);
        s.HomeScoring.ScoringDrought.Flag.Should().BeTrue();
    }

    // ── B. Results & form ────────────────────────────────────────────────────

    [Fact]
    public void FormString_And_Points_FromLast5()
    {
        var history = new List<Fixture>
        {
            Match(HomeId, true, 2, 0, 3),   // W
            Match(HomeId, false, 1, 1, 10), // D
            Match(HomeId, true, 0, 1, 17),  // L
            Match(HomeId, false, 3, 1, 24), // W
            Match(HomeId, true, 2, 2, 31)   // D
        };

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeForm.FormLast5.Label.Should().Contain("WDLWD");
        s.HomeForm.FormLast5.Value.Should().Be(8); // 3+1+0+3+1
    }

    [Fact]
    public void FormDelta_TrendingUp_Flagged()
    {
        // Last 5: all wins (3.0 PPG); season PPG from Team: 1.5 → delta 1.5
        var history = Enumerable.Range(1, 5)
            .Select(i => Match(HomeId, true, 2, 0, i * 7)).ToList();
        var team = MakeTeam(HomeId, rank: 8, points: 30, played: 20); // 1.5 PPG

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history, homeTeam: team), Opt);

        s.HomeForm.FormDelta.Value.Should().BeApproximately(1.5, 1e-9);
        s.HomeForm.FormDelta.Flag.Should().BeTrue();
        s.HomeForm.FormDelta.Label.Should().Contain("trending up");
    }

    [Fact]
    public void Streaks_WinlessUnbeatenLosing()
    {
        var losing = Enumerable.Range(1, 4)
            .Select(i => Match(HomeId, true, 0, 1, i * 7)).ToList();

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: losing), Opt);

        s.HomeForm.LosingStreak.Value.Should().Be(4);
        s.HomeForm.LosingStreak.Flag.Should().BeTrue();
        s.HomeForm.WinlessStreak.Value.Should().Be(4);
        s.HomeForm.UnbeatenStreak.Value.Should().Be(0);
    }

    [Fact]
    public void MentalitySignals_FromHalfTimeProxy()
    {
        var history = new List<Fixture>
        {
            // Trailed 0-1 at HT, won 2-1 → 3 pts from losing position
            Match(HomeId, true, 2, 1, 3, htFor: 0, htAgainst: 1),
            // Led 1-0 at HT, drew 1-1 → dropped 2 pts from winning position
            Match(HomeId, true, 1, 1, 10, htFor: 1, htAgainst: 0),
            // Led 2-0 at HT, lost 2-3 → dropped 3 pts
            Match(HomeId, false, 2, 3, 17, htFor: 2, htAgainst: 0)
        };

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeForm.PointsFromLosingPositions.Value.Should().Be(3);
        s.HomeForm.PointsDroppedFromWinning.Value.Should().Be(5);
        s.HomeForm.PointsDroppedFromWinning.Flag.Should().BeTrue();
    }

    [Fact]
    public void TightGameShare_OneGoalMargins_Flagged()
    {
        var history = Enumerable.Range(1, 10)
            .Select(i => Match(HomeId, true, 1, i % 2 == 0 ? 0 : 2, i * 7)).ToList(); // all 1-goal margins

        var s = StrategicSignalCalculator.Compute(Inputs(homeHistory: history), Opt);

        s.HomeForm.TightGameShareLast10.Value.Should().Be(1.0);
        s.HomeForm.TightGameShareLast10.Flag.Should().BeTrue();
    }

    // ── C. Table context ─────────────────────────────────────────────────────

    [Fact]
    public void RankAndPpgGaps_Flagged()
    {
        var home = MakeTeam(HomeId, rank: 2, points: 60, played: 28, gd: 30);   // 2.14 PPG
        var away = MakeTeam(AwayId, rank: 17, points: 25, played: 28, gd: -20); // 0.89 PPG

        var s = StrategicSignalCalculator.Compute(Inputs(homeTeam: home, awayTeam: away), Opt);

        s.Table.RankGap.Value.Should().Be(15);
        s.Table.RankGap.Flag.Should().BeTrue();
        s.Table.PpgGap.Flag.Should().BeTrue();
        s.Table.HomeTitleRace.Flag.Should().BeTrue();
        s.Table.AwayRelegationZone.Flag.Should().BeFalse("rank 17 of 20 with 3 relegation spots is 18+");
    }

    [Fact]
    public void RelegationZone_UsesLeagueProfile()
    {
        var away = MakeTeam(AwayId, rank: 18, points: 20, played: 28);
        var home = MakeTeam(HomeId, rank: 10, points: 38, played: 28);

        var s = StrategicSignalCalculator.Compute(Inputs(homeTeam: home, awayTeam: away), Opt);

        s.Table.AwayRelegationZone.Flag.Should().BeTrue("rank 18/20 in the PL is in the bottom 3");
    }

    [Fact]
    public void DeadRubber_And_MotivationAsymmetry_InRunIn()
    {
        // Matchday 34+ of 38 (run-in): home mid-table safe, away fighting relegation
        var home = MakeTeam(HomeId, rank: 11, points: 45, played: 34);
        var away = MakeTeam(AwayId, rank: 19, points: 26, played: 34);

        var s = StrategicSignalCalculator.Compute(Inputs(homeTeam: home, awayTeam: away), Opt);

        s.Table.SeasonPhase.Label.Should().Contain("run-in");
        s.Table.HomeDeadRubber.Flag.Should().BeTrue();
        s.Table.AwayDeadRubber.Flag.Should().BeFalse();
        s.Table.MotivationAsymmetry.Flag.Should().BeTrue();
        s.Table.RunInWithStakes.Flag.Should().BeTrue();
    }

    [Fact]
    public void SeasonPhase_Opening()
    {
        var home = MakeTeam(HomeId, rank: 5, points: 6, played: 3);
        var away = MakeTeam(AwayId, rank: 12, points: 3, played: 3);

        var s = StrategicSignalCalculator.Compute(Inputs(homeTeam: home, awayTeam: away), Opt);

        s.Table.SeasonPhase.Label.Should().Contain("opening");
        s.Table.HomeDeadRubber.Flag.Should().BeFalse("dead rubber only exists in the run-in");
    }

    [Fact]
    public void PlayoffZone_Championship()
    {
        var fixture = TheFixture();
        fixture.LeagueId = 40;
        var home = MakeTeam(HomeId, rank: 4, points: 70, played: 40);
        var away = MakeTeam(AwayId, rank: 15, points: 50, played: 40);

        var s = StrategicSignalCalculator.Compute(
            Inputs(homeTeam: home, awayTeam: away, fixture: fixture), Opt);

        s.Table.HomePlayoffZone.Flag.Should().BeTrue("rank 4 is in the Championship playoff range 3-6");
    }

    // ── D. Head-to-head ──────────────────────────────────────────────────────

    [Fact]
    public void H2HRates_AvgGoals_And_Margin()
    {
        var h2h = new List<Fixture>
        {
            H2HMatch(HomeId, AwayId, 2, 1, 100),  // BTTS, O2.5
            H2HMatch(AwayId, HomeId, 3, 1, 300),  // BTTS, O2.5
            H2HMatch(HomeId, AwayId, 0, 0, 500),  // neither
            H2HMatch(AwayId, HomeId, 2, 2, 700),  // BTTS, O2.5
            H2HMatch(HomeId, AwayId, 1, 0, 900)   // neither
        };

        var s = StrategicSignalCalculator.Compute(Inputs(h2h: h2h), Opt);

        s.H2H.BttsRateLast5.Value.Should().BeApproximately(0.6, 1e-9);
        s.H2H.BttsRateLast5.Flag.Should().BeTrue();
        s.H2H.Over25RateLast5.Value.Should().BeApproximately(0.6, 1e-9);
        s.H2H.AvgTotalGoals.Value.Should().BeApproximately(2.4, 1e-9);
        s.H2H.AvgGoalMargin.Value.Should().BeApproximately(0.8, 1e-9); // margins 1,2,0,0,1
        s.H2H.SampleSize.Should().Be(5);
    }

    [Fact]
    public void H2H_HomeVenueRecord_OnlyThisStadium()
    {
        var h2h = new List<Fixture>
        {
            H2HMatch(HomeId, AwayId, 2, 0, 100),  // host won here
            H2HMatch(AwayId, HomeId, 3, 0, 300),  // other stadium — excluded
            H2HMatch(HomeId, AwayId, 1, 0, 500)   // host won here
        };

        var s = StrategicSignalCalculator.Compute(Inputs(h2h: h2h), Opt);

        s.H2H.HomeVenueHomeWinRate.Value.Should().Be(1.0);
        s.H2H.HomeVenueHomeWinRate.Flag.Should().BeTrue();
        s.H2H.HomeVenueAvgGoals.Value.Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void H2H_Dominance_OneSideUnbeaten()
    {
        var h2h = Enumerable.Range(1, 5)
            .Select(i => H2HMatch(HomeId, AwayId, 2, 0, i * 150)).ToList(); // home always wins

        var s = StrategicSignalCalculator.Compute(Inputs(h2h: h2h), Opt);

        s.H2H.Dominance.Flag.Should().BeTrue();
        s.H2H.Dominance.Label.Should().Contain("home side");
    }

    [Fact]
    public void H2H_StyleClash_GoalsDivergeFromSeasonNorm()
    {
        // Both teams average ~1 total goal per match, but H2H averages 4+
        var quiet = Enumerable.Range(1, 10).Select(i => Match(HomeId, true, 1, 0, i * 7)).ToList();
        var quietAway = Enumerable.Range(1, 10).Select(i => Match(AwayId, false, 0, 1, i * 7)).ToList();
        var wildH2H = Enumerable.Range(1, 5).Select(i => H2HMatch(HomeId, AwayId, 3, 2, i * 150)).ToList();

        var s = StrategicSignalCalculator.Compute(
            Inputs(homeHistory: quiet, awayHistory: quietAway, h2h: wildH2H), Opt);

        s.H2H.StyleClash.Flag.Should().BeTrue();
        s.H2H.StyleClash.Value.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void DerbyFlag_FromFixture()
    {
        var s = StrategicSignalCalculator.Compute(Inputs(fixture: TheFixture(derby: true)), Opt);
        s.H2H.Derby.Flag.Should().BeTrue();
    }

    // ── E. Schedule & fatigue ────────────────────────────────────────────────

    [Fact]
    public void RestDays_Gap_And_Congestion()
    {
        var homeHistory = new List<Fixture>
        {
            Match(HomeId, true, 1, 0, 2),   // 2 days rest
            Match(HomeId, false, 1, 0, 5),
            Match(HomeId, true, 1, 0, 9),
            Match(HomeId, false, 1, 0, 13)  // 4 matches in 14 days
        };
        var awayHistory = new List<Fixture> { Match(AwayId, false, 1, 0, 8) }; // 8 days rest

        var s = StrategicSignalCalculator.Compute(
            Inputs(homeHistory: homeHistory, awayHistory: awayHistory), Opt);

        s.Schedule.HomeRestDays.Value.Should().Be(2);
        s.Schedule.HomeRestDays.Flag.Should().BeTrue("3 days or fewer is short rest");
        s.Schedule.AwayRestDays.Value.Should().Be(8);
        s.Schedule.RestDayGap.Value.Should().Be(-6);
        s.Schedule.RestDayGap.Flag.Should().BeTrue();
        s.Schedule.HomeMatchesLast14Days.Value.Should().Be(4);
        s.Schedule.HomeMatchesLast14Days.Flag.Should().BeTrue();
        s.Schedule.AwayMatchesLast14Days.Flag.Should().BeFalse();
    }

    [Fact]
    public void Tier2Proximity_FlagsFromInputs()
    {
        var s = StrategicSignalCalculator.Compute(Inputs(homeTier2: true), Opt);

        s.Schedule.HomeTier2Within4Days.Flag.Should().BeTrue();
        s.Schedule.AwayTier2Within4Days.Flag.Should().BeFalse();
    }

    // ── F. Availability (graceful degradation) ───────────────────────────────

    [Fact]
    public void Availability_DegradesGracefully_NoDataSynced()
    {
        var s = StrategicSignalCalculator.Compute(Inputs(), Opt);

        s.Availability.DataAvailable.Should().BeFalse();
        s.Availability.HomeKeyAbsences.Flag.Should().BeFalse();
        s.Availability.HomeKeyAbsences.Label.Should().Contain("No availability data");
    }

    // ── G. Market signals ────────────────────────────────────────────────────

    [Fact]
    public void ModelMarketDivergence_Flagged_WhenLarge()
    {
        var dc = new PoissonModel { Over25 = 0.75, BTTS = 0.5, HomeWin = 0.5, Draw = 0.3, AwayWin = 0.2, IsValid = true };
        // Symmetric O/U odds → market ~0.5 → divergence 0.25
        var fixture = TheFixture(over: 2.0, under: 2.0);

        var s = StrategicSignalCalculator.Compute(Inputs(fixture: fixture, dc: dc), Opt);

        s.Market.DivergenceOver25.Value.Should().BeApproximately(0.25, 0.01);
        s.Market.DivergenceOver25.Flag.Should().BeTrue();
    }

    [Fact]
    public void FavoriteOddsBand_Heavy()
    {
        var fixture = TheFixture(homeOdds: 1.25, drawOdds: 6.0, awayOdds: 12.0);

        var s = StrategicSignalCalculator.Compute(Inputs(fixture: fixture), Opt);

        s.Market.FavoriteOddsBand.Flag.Should().BeTrue();
        s.Market.FavoriteOddsBand.Label.Should().Contain("heavy favorite");
    }

    [Fact]
    public void TrapPattern_MarketFavorsWorseRankedSide()
    {
        // Away side is the favorite by odds but ranked 10 places worse
        var fixture = TheFixture(homeOdds: 3.2, drawOdds: 3.4, awayOdds: 2.0);
        var home = MakeTeam(HomeId, rank: 5, points: 50, played: 28);
        var away = MakeTeam(AwayId, rank: 15, points: 30, played: 28);

        var s = StrategicSignalCalculator.Compute(
            Inputs(fixture: fixture, homeTeam: home, awayTeam: away), Opt);

        s.Market.Trap.Flag.Should().BeTrue();
        s.Market.Trap.Value.Should().Be(10);
    }

    [Fact]
    public void OpeningDrift_DegradesGracefully_WithoutQuoteHistory()
    {
        var s = StrategicSignalCalculator.Compute(Inputs(), Opt);
        s.Market.OpeningDrift.Flag.Should().BeFalse();
        s.Market.OpeningDrift.Label.Should().Contain("drift unavailable");
    }

    [Fact]
    public void OpeningDrift_FromQuoteHistory_FlagsBigMoves()
    {
        var drift = new SoccerAi.Application.Services.OddsDriftResult(
            FavoriteDriftPct: -0.12, Over25DriftPct: 0.03,
            FavoriteDirection: "shortening (money on favorite)");

        var inputs = Inputs() with { OddsDrift = drift };
        var s = StrategicSignalCalculator.Compute(inputs, Opt);

        s.Market.OpeningDrift.Value.Should().Be(-0.12);
        s.Market.OpeningDrift.Flag.Should().BeTrue("12% move exceeds the 10% flag threshold");
        s.Market.OpeningDrift.Label.Should().Contain("shortening");
    }

    // ── H. League profile ────────────────────────────────────────────────────

    [Fact]
    public void LeagueBaseRates_And_TeamDeviation()
    {
        // League: 4 of 10 over 2.5 (40%); home team: 10 of 10 over (100%) → deviation +60pp
        var league = Enumerable.Range(1, 10)
            .Select(i => H2HMatch(300 + i, 400 + i, i <= 4 ? 2 : 1, i <= 4 ? 1 : 0, i * 5)).ToList();
        var homeHistory = Enumerable.Range(1, 10)
            .Select(i => Match(HomeId, true, 3, 1, i * 7)).ToList();

        var s = StrategicSignalCalculator.Compute(
            Inputs(homeHistory: homeHistory, league: league), Opt);

        s.League.LeagueOver25Rate.Value.Should().BeApproximately(0.4, 1e-9);
        s.League.HomeOver25VsLeague.Value.Should().BeApproximately(0.6, 1e-9);
        s.League.HomeOver25VsLeague.Flag.Should().BeTrue();
    }

    [Fact]
    public void LeagueVolatility_AsSignalNotAdjustment()
    {
        var s = StrategicSignalCalculator.Compute(Inputs(volatility: 0.25), Opt);

        s.League.LeagueVolatility.Value.Should().Be(0.25);
        s.League.LeagueVolatility.Flag.Should().BeTrue("0.25 >= high-volatility threshold");
    }
}
