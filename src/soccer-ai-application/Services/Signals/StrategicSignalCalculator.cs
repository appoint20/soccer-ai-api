using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Signals;

/// <summary>
/// Everything the calculator needs — plain data, no I/O. All histories are
/// finished matches strictly BEFORE kickoff, ordered newest first.
/// </summary>
public sealed record SignalInputs(
    Fixture Fixture,
    Team? HomeTeam,
    Team? AwayTeam,
    IReadOnlyList<Fixture> HomeHistory,
    IReadOnlyList<Fixture> AwayHistory,
    IReadOnlyList<Fixture> H2HHistory,
    IReadOnlyList<Fixture> LeagueSeasonMatches,
    bool HomeTier2Within4Days,
    bool AwayTier2Within4Days,
    PoissonModel? DcModel,
    double LeagueVolatility);

/// <summary>
/// Pure, stateless signal computation (fully unit-testable with synthetic
/// fixture histories). Signals are facts; they never touch probabilities.
/// </summary>
public static class StrategicSignalCalculator
{
    public static StrategicSignals Compute(SignalInputs inputs, StrategyOptions opt)
    {
        var f = inputs.Fixture;
        return new StrategicSignals
        {
            HomeScoring = ComputeScoring(f.HomeTeamId, inputs.HomeHistory, isHomeSide: true, inputs.HomeTeam, opt),
            AwayScoring = ComputeScoring(f.AwayTeamId, inputs.AwayHistory, isHomeSide: false, inputs.AwayTeam, opt),
            HomeForm = ComputeForm(f.HomeTeamId, inputs.HomeHistory, isHomeSide: true, inputs.HomeTeam, opt),
            AwayForm = ComputeForm(f.AwayTeamId, inputs.AwayHistory, isHomeSide: false, inputs.AwayTeam, opt),
            Table = ComputeTable(f, inputs.HomeTeam, inputs.AwayTeam, opt),
            H2H = ComputeH2H(f, inputs.H2HHistory, inputs.HomeHistory, inputs.AwayHistory, opt),
            Schedule = ComputeSchedule(f, inputs, opt),
            Availability = new AvailabilitySignals(), // graceful degradation: not synced yet
            Market = ComputeMarket(f, inputs.HomeTeam, inputs.AwayTeam, inputs.DcModel, opt),
            League = ComputeLeague(f, inputs, opt),
            ComputedAtUtc = DateTimeOffset.UtcNow
        };
    }

    // ── A. Scoring & conceding ───────────────────────────────────────────────

    private static ScoringSignals ComputeScoring(
        int teamId, IReadOnlyList<Fixture> history, bool isHomeSide, Team? team, StrategyOptions opt)
    {
        var venue = history.Where(m => isHomeSide ? m.HomeTeamId == teamId : m.AwayTeamId == teamId).ToList();
        var side = isHomeSide ? "home" : "away";

        SignalValue ScoredIn(IReadOnlyList<Fixture> src, int n, string where)
        {
            var window = src.Take(n).ToList();
            var count = window.Count(m => GoalsFor(m, teamId) > 0);
            return SignalValue.Of(count, window.Count == n && count == n,
                $"Scored in {count}/{window.Count} of last {n} {where} matches");
        }

        SignalValue ConcededIn(IReadOnlyList<Fixture> src, int n, string where)
        {
            var window = src.Take(n).ToList();
            var count = window.Count(m => GoalsAgainst(m, teamId) > 0);
            return SignalValue.Of(count, window.Count == n && count == n,
                $"Conceded in {count}/{window.Count} of last {n} {where} matches");
        }

        SignalValue Rate(IReadOnlyList<Fixture> src, int n, Func<Fixture, bool> pred, string what)
        {
            var window = src.Take(n).ToList();
            if (window.Count == 0) return SignalValue.Unavailable($"No {side} matches for {what}");
            var rate = (double)window.Count(pred) / window.Count;
            return SignalValue.Of(rate, rate >= opt.HighRateFlag,
                $"{what} in {rate:P0} of last {window.Count} {side} matches");
        }

        var last5Venue = venue.Take(opt.MidWindow).ToList();
        var fts = last5Venue.Count(m => GoalsFor(m, teamId) == 0);
        var cleanSheets = last5Venue.Count(m => GoalsAgainst(m, teamId) == 0);

        // Attack/defense trend: last-5 venue avg vs season (all matches) avg
        var seasonFor = history.Count > 0 ? history.Average(m => (double)GoalsFor(m, teamId)) : 0;
        var seasonAgainst = history.Count > 0 ? history.Average(m => (double)GoalsAgainst(m, teamId)) : 0;
        var last5For = last5Venue.Count > 0 ? last5Venue.Average(m => (double)GoalsFor(m, teamId)) : seasonFor;
        var last5Against = last5Venue.Count > 0 ? last5Venue.Average(m => (double)GoalsAgainst(m, teamId)) : seasonAgainst;

        // Half-goal profile from last LongWindow matches (any venue)
        var lastLong = history.Take(opt.LongWindow).ToList();
        var firstHalfGoals = lastLong.Sum(m => FirstHalfGoalsFor(m, teamId));
        var totalGoals = lastLong.Sum(m => GoalsFor(m, teamId));
        var fhShare = totalGoals > 0 ? (double)firstHalfGoals / totalGoals : 0;
        var shShare = totalGoals > 0 ? 1 - fhShare : 0;

        var last5All = history.Take(opt.MidWindow).ToList();
        var avgTotal = last5All.Count > 0 ? last5All.Average(m => (double)(m.HomeGoal + m.AwayGoal)) : 0;

        var drought = CountStreak(history, m => GoalsFor(m, teamId) == 0);

        return new ScoringSignals
        {
            ScoredInLast3Venue = ScoredIn(venue, opt.ShortWindow, side),
            ScoredInLast5Venue = ScoredIn(venue, opt.MidWindow, side),
            ScoredInLast3Overall = ScoredIn(history, opt.ShortWindow, "overall"),
            ScoredInLast5Overall = ScoredIn(history, opt.MidWindow, "overall"),
            ConcededInLast3Venue = ConcededIn(venue, opt.ShortWindow, side),
            ConcededInLast5Venue = ConcededIn(venue, opt.MidWindow, side),
            ConcededInLast3Overall = ConcededIn(history, opt.ShortWindow, "overall"),
            ConcededInLast5Overall = ConcededIn(history, opt.MidWindow, "overall"),
            FailedToScoreLast5Venue = SignalValue.Of(fts, fts >= opt.FailedToScoreFlagCount,
                $"Failed to score in {fts} of last {last5Venue.Count} {side} matches"),
            CleanSheetsLast5Venue = SignalValue.Of(cleanSheets, cleanSheets >= opt.CleanSheetFlagCount,
                $"{cleanSheets} clean sheets in last {last5Venue.Count} {side} matches"),
            AttackTrend = SignalValue.Of(last5For - seasonFor,
                Math.Abs(last5For - seasonFor) >= opt.GoalTrendFlagDelta,
                $"Scoring {last5For:F2}/game last 5 {side} vs {seasonFor:F2} season"),
            DefenseTrend = SignalValue.Of(last5Against - seasonAgainst,
                Math.Abs(last5Against - seasonAgainst) >= opt.GoalTrendFlagDelta,
                $"Conceding {last5Against:F2}/game last 5 {side} vs {seasonAgainst:F2} season"),
            Over25RateLast5Venue = Rate(venue, opt.MidWindow, m => m.HomeGoal + m.AwayGoal > 2, "Over 2.5"),
            Over25RateLast10Venue = Rate(venue, opt.LongWindow, m => m.HomeGoal + m.AwayGoal > 2, "Over 2.5"),
            BttsRateLast5Venue = Rate(venue, opt.MidWindow, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, "BTTS"),
            BttsRateLast10Venue = Rate(venue, opt.LongWindow, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, "BTTS"),
            Under25RateLast5Venue = Rate(venue, opt.MidWindow, m => m.HomeGoal + m.AwayGoal < 3, "Under 2.5"),
            Under25RateLast10Venue = Rate(venue, opt.LongWindow, m => m.HomeGoal + m.AwayGoal < 3, "Under 2.5"),
            FirstHalfGoalShare = SignalValue.Of(fhShare, fhShare >= opt.SecondHalfShareFlag,
                $"{fhShare:P0} of goals before half-time (last {lastLong.Count})"),
            SecondHalfGoalShare = SignalValue.Of(shShare, shShare >= opt.SecondHalfShareFlag,
                $"{shShare:P0} of goals after half-time (last {lastLong.Count})"),
            AvgTotalGoalsLast5 = SignalValue.Of(avgTotal, avgTotal >= opt.ChaosAvgGoalsFlag,
                $"Avg {avgTotal:F1} total goals in last {last5All.Count} matches"),
            ScoringDrought = SignalValue.Of(drought, drought >= opt.ScoringDroughtFlagLength,
                drought > 0 ? $"No goal in last {drought} matches" : "No active scoring drought")
        };
    }

    // ── B. Results & form ────────────────────────────────────────────────────

    private static FormSignals ComputeForm(
        int teamId, IReadOnlyList<Fixture> history, bool isHomeSide, Team? team, StrategyOptions opt)
    {
        var side = isHomeSide ? "home" : "away";
        var last5 = history.Take(opt.MidWindow).ToList();
        var formString = string.Concat(last5.Select(m => ResultChar(m, teamId)));
        var pointsLast5 = last5.Sum(m => PointsFor(m, teamId));

        var venue = history.Where(m => isHomeSide ? m.HomeTeamId == teamId : m.AwayTeamId == teamId)
            .Take(opt.MidWindow).ToList();
        var ppgVenue = venue.Count > 0 ? venue.Average(m => (double)PointsFor(m, teamId)) : 0;

        var seasonPpg = team is { Played: > 0 }
            ? (double)team.Points / team.Played
            : history.Count > 0 ? history.Average(m => (double)PointsFor(m, teamId)) : 0;

        var ppgLast5 = last5.Count > 0 ? last5.Average(m => (double)PointsFor(m, teamId)) : 0;
        var formDelta = ppgLast5 - seasonPpg;

        var winless = CountStreak(history, m => PointsFor(m, teamId) < 3);
        var unbeaten = CountStreak(history, m => PointsFor(m, teamId) > 0);
        var losing = CountStreak(history, m => PointsFor(m, teamId) == 0);

        // Mentality via half-time score proxy (minute data is not stored)
        var last10 = history.Take(opt.LongWindow).ToList();
        var fromLosing = last10
            .Where(m => HtGoalsFor(m, teamId) < HtGoalsAgainst(m, teamId))
            .Sum(m => PointsFor(m, teamId));
        var droppedFromWinning = last10
            .Where(m => HtGoalsFor(m, teamId) > HtGoalsAgainst(m, teamId))
            .Sum(m => 3 - PointsFor(m, teamId));

        var tight = last10.Count > 0
            ? (double)last10.Count(m => Math.Abs(m.HomeGoal - m.AwayGoal) == 1) / last10.Count
            : 0;

        return new FormSignals
        {
            FormLast5 = SignalValue.Of(pointsLast5, pointsLast5 >= 12,
                $"Form {formString} ({pointsLast5} pts from last {last5.Count})"),
            FormDelta = SignalValue.Of(formDelta, Math.Abs(formDelta) >= opt.FormDeltaFlag,
                $"PPG last 5 {ppgLast5:F2} vs season {seasonPpg:F2} ({(formDelta >= 0 ? "trending up" : "trending down")})"),
            PpgLast5Venue = SignalValue.Of(ppgVenue, ppgVenue >= 2.0,
                $"{ppgVenue:F2} PPG in last {venue.Count} {side} matches"),
            SeasonPpg = SignalValue.Of(seasonPpg, seasonPpg >= 2.0, $"Season PPG {seasonPpg:F2}"),
            WinlessStreak = SignalValue.Of(winless, winless >= opt.StreakFlagLength,
                $"{winless} matches without a win"),
            UnbeatenStreak = SignalValue.Of(unbeaten, unbeaten >= opt.StreakFlagLength,
                $"{unbeaten} matches unbeaten"),
            LosingStreak = SignalValue.Of(losing, losing >= opt.StreakFlagLength,
                $"{losing} consecutive defeats"),
            PointsFromLosingPositions = SignalValue.Of(fromLosing, fromLosing >= 4,
                $"{fromLosing} pts recovered from half-time deficits (last {last10.Count})"),
            PointsDroppedFromWinning = SignalValue.Of(droppedFromWinning, droppedFromWinning >= 4,
                $"{droppedFromWinning} pts dropped from half-time leads (last {last10.Count})"),
            TightGameShareLast10 = SignalValue.Of(tight, tight >= opt.TightGameShareFlag,
                $"{tight:P0} one-goal margins in last {last10.Count}")
        };
    }

    // ── C. Table context ─────────────────────────────────────────────────────

    private static TableContextSignals ComputeTable(Fixture f, Team? home, Team? away, StrategyOptions opt)
    {
        if (home == null || away == null || home.Played == 0 || away.Played == 0)
            return new TableContextSignals
            {
                HomeRank = SignalValue.Unavailable("Standings not available"),
                AwayRank = SignalValue.Unavailable("Standings not available")
            };

        var profile = opt.GetProfile(f.LeagueId);
        var homePpg = (double)home.Points / home.Played;
        var awayPpg = (double)away.Points / away.Played;
        var rankGap = Math.Abs(home.Rank - away.Rank);
        var ppgGap = Math.Abs(homePpg - awayPpg);

        (bool Title, bool Euro, bool Playoff, bool Relegation) Zones(Team t) => (
            t.Rank <= profile.TitleSpots,
            profile.EuropeanSpots > 0 && t.Rank <= profile.EuropeanSpots,
            profile.PlayoffStart > 0 && t.Rank >= profile.PlayoffStart && t.Rank <= profile.PlayoffEnd,
            t.Rank > profile.LeagueSize - profile.RelegationSpots);

        var hz = Zones(home);
        var az = Zones(away);

        var matchday = Math.Max(home.Played, away.Played) + 1;
        var isOpening = matchday <= opt.OpeningPhaseMatchdays;
        var isRunIn = matchday > profile.TotalMatchdays - opt.RunInMatchdays;
        var phase = isOpening ? 0 : isRunIn ? 2 : 1;
        var phaseLabel = isOpening ? "opening" : isRunIn ? "run-in" : "main";

        bool InAnyZone((bool T, bool E, bool P, bool R) z) => z.T || z.E || z.P || z.R;
        var homeDead = isRunIn && !InAnyZone(hz);
        var awayDead = isRunIn && !InAnyZone(az);

        bool MustWin((bool T, bool E, bool P, bool R) z) => z.T || z.P || z.R;
        var asymmetry = isRunIn && (MustWin(hz) && awayDead || MustWin(az) && homeDead);

        var stakes = isRunIn && (MustWin(hz) || MustWin(az));

        return new TableContextSignals
        {
            HomeRank = SignalValue.Of(home.Rank, false, $"Home rank {home.Rank}/{profile.LeagueSize}"),
            AwayRank = SignalValue.Of(away.Rank, false, $"Away rank {away.Rank}/{profile.LeagueSize}"),
            RankGap = SignalValue.Of(rankGap, rankGap >= opt.RankGapFlag, $"Rank gap {rankGap}"),
            PpgGap = SignalValue.Of(ppgGap, ppgGap >= opt.PpgGapFlag,
                $"PPG gap {ppgGap:F2} ({homePpg:F2} vs {awayPpg:F2})"),
            GoalDifferenceGap = SignalValue.Of(home.GoalsDiff - away.GoalsDiff, false,
                $"GD {home.GoalsDiff} vs {away.GoalsDiff}"),
            HomeTitleRace = SignalValue.Of(home.Rank, hz.Title, hz.Title ? "Home in title race (top 3)" : "Home not in title race"),
            AwayTitleRace = SignalValue.Of(away.Rank, az.Title, az.Title ? "Away in title race (top 3)" : "Away not in title race"),
            HomeEuropeanSpots = SignalValue.Of(home.Rank, hz.Euro, hz.Euro ? "Home in European spots" : "Home outside European spots"),
            AwayEuropeanSpots = SignalValue.Of(away.Rank, az.Euro, az.Euro ? "Away in European spots" : "Away outside European spots"),
            HomePlayoffZone = SignalValue.Of(home.Rank, hz.Playoff, hz.Playoff ? "Home in playoff zone" : "Home outside playoff zone"),
            AwayPlayoffZone = SignalValue.Of(away.Rank, az.Playoff, az.Playoff ? "Away in playoff zone" : "Away outside playoff zone"),
            HomeRelegationZone = SignalValue.Of(home.Rank, hz.Relegation, hz.Relegation ? "Home in relegation zone" : "Home safe from relegation zone"),
            AwayRelegationZone = SignalValue.Of(away.Rank, az.Relegation, az.Relegation ? "Away in relegation zone" : "Away safe from relegation zone"),
            HomeDeadRubber = SignalValue.Of(home.Rank, homeDead, homeDead ? "Home mid-table with nothing to play for" : "Home has stakes or season not in run-in"),
            AwayDeadRubber = SignalValue.Of(away.Rank, awayDead, awayDead ? "Away mid-table with nothing to play for" : "Away has stakes or season not in run-in"),
            MotivationAsymmetry = SignalValue.Of(asymmetry ? 1 : 0, asymmetry,
                asymmetry ? "One side must win, the other has nothing to play for" : "No motivation asymmetry"),
            SeasonPhase = SignalValue.Of(phase, isRunIn, $"Season phase: {phaseLabel} (matchday ~{matchday})"),
            RunInWithStakes = SignalValue.Of(stakes ? 1 : 0, stakes,
                stakes ? "Run-in with title/promotion/relegation stakes" : "No run-in stakes")
        };
    }

    // ── D. Head-to-head ──────────────────────────────────────────────────────

    private static HeadToHeadSignals ComputeH2H(
        Fixture f, IReadOnlyList<Fixture> h2h,
        IReadOnlyList<Fixture> homeHistory, IReadOnlyList<Fixture> awayHistory, StrategyOptions opt)
    {
        if (h2h.Count == 0)
            return new HeadToHeadSignals
            {
                BttsRateLast5 = SignalValue.Unavailable("No head-to-head history"),
                Derby = SignalValue.Of(f.IsDerby ? 1 : 0, f.IsDerby, f.IsDerby ? "Derby fixture" : "Not a derby"),
                SampleSize = 0
            };

        SignalValue RateOf(int n, Func<Fixture, bool> pred, string what)
        {
            var window = h2h.Take(n).ToList();
            var rate = (double)window.Count(pred) / window.Count;
            return SignalValue.Of(rate, rate >= opt.H2HHighRateFlag,
                $"{what} in {rate:P0} of last {window.Count} H2H meetings");
        }

        var last5 = h2h.Take(opt.H2HWindow).ToList();
        var avgGoals = last5.Average(m => (double)(m.HomeGoal + m.AwayGoal));
        var avgMargin = last5.Average(m => (double)Math.Abs(m.HomeGoal - m.AwayGoal));

        // This stadium, this pairing
        var atThisVenue = h2h.Where(m => m.HomeTeamId == f.HomeTeamId).Take(opt.H2HWindow).ToList();
        var venueWins = atThisVenue.Count(m => m.HomeGoal > m.AwayGoal);
        var venueAvgGoals = atThisVenue.Count > 0 ? atThisVenue.Average(m => (double)(m.HomeGoal + m.AwayGoal)) : 0;

        // Dominance: either side unbeaten across last N meetings
        var homeUnbeaten = last5.Count(m => PointsFor(m, f.HomeTeamId) > 0);
        var awayUnbeaten = last5.Count(m => PointsFor(m, f.AwayTeamId) > 0);
        var dominant = last5.Count >= opt.DominanceUnbeatenCount &&
                       (homeUnbeaten == last5.Count || awayUnbeaten == last5.Count);
        var dominantSide = homeUnbeaten == last5.Count ? "home side" : "away side";

        // Style clash: H2H goals diverge from both teams' season averages
        var homeSeasonAvg = homeHistory.Count > 0 ? homeHistory.Average(m => (double)(m.HomeGoal + m.AwayGoal)) : 0;
        var awaySeasonAvg = awayHistory.Count > 0 ? awayHistory.Average(m => (double)(m.HomeGoal + m.AwayGoal)) : 0;
        var seasonAvg = (homeSeasonAvg + awaySeasonAvg) / 2;
        var clashDelta = avgGoals - seasonAvg;
        var clash = homeHistory.Count > 0 && awayHistory.Count > 0 &&
                    Math.Abs(clashDelta) > opt.StyleClashGoalDiff;

        return new HeadToHeadSignals
        {
            BttsRateLast5 = RateOf(opt.H2HWindow, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, "BTTS"),
            BttsRateLast10 = RateOf(opt.H2HLongWindow, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, "BTTS"),
            Over25RateLast5 = RateOf(opt.H2HWindow, m => m.HomeGoal + m.AwayGoal > 2, "Over 2.5"),
            Over25RateLast10 = RateOf(opt.H2HLongWindow, m => m.HomeGoal + m.AwayGoal > 2, "Over 2.5"),
            AvgTotalGoals = SignalValue.Of(avgGoals, avgGoals >= opt.ChaosAvgGoalsFlag,
                $"Avg {avgGoals:F1} goals in last {last5.Count} H2H"),
            AvgGoalMargin = SignalValue.Of(avgMargin, avgMargin >= 2,
                $"Avg margin {avgMargin:F1} goals in last {last5.Count} H2H"),
            HomeVenueHomeWinRate = atThisVenue.Count > 0
                ? SignalValue.Of((double)venueWins / atThisVenue.Count, venueWins == atThisVenue.Count,
                    $"Host won {venueWins}/{atThisVenue.Count} H2H at this stadium")
                : SignalValue.Unavailable("No H2H at this stadium"),
            HomeVenueAvgGoals = atThisVenue.Count > 0
                ? SignalValue.Of(venueAvgGoals, venueAvgGoals >= opt.ChaosAvgGoalsFlag,
                    $"Avg {venueAvgGoals:F1} goals in H2H at this stadium")
                : SignalValue.Unavailable("No H2H at this stadium"),
            Dominance = SignalValue.Of(Math.Max(homeUnbeaten, awayUnbeaten), dominant,
                dominant ? $"The {dominantSide} is unbeaten in the last {last5.Count} H2H" : "No H2H dominance pattern"),
            StyleClash = SignalValue.Of(clashDelta, clash,
                clash
                    ? $"H2H goals ({avgGoals:F1}) diverge from season norm ({seasonAvg:F1}) — plays out differently"
                    : "H2H matches the teams' usual goal profile"),
            Derby = SignalValue.Of(f.IsDerby ? 1 : 0, f.IsDerby, f.IsDerby ? "Derby fixture" : "Not a derby"),
            SampleSize = h2h.Count
        };
    }

    // ── E. Schedule & fatigue ────────────────────────────────────────────────

    private static ScheduleSignals ComputeSchedule(Fixture f, SignalInputs inputs, StrategyOptions opt)
    {
        double RestDays(IReadOnlyList<Fixture> history) =>
            history.Count > 0 ? Math.Min((f.Date - history[0].Date).TotalDays, 14) : 14;

        int In14Days(IReadOnlyList<Fixture> history) =>
            history.Count(m => (f.Date - m.Date).TotalDays <= 14);

        var homeRest = RestDays(inputs.HomeHistory);
        var awayRest = RestDays(inputs.AwayHistory);
        var gap = homeRest - awayRest;
        var homeCongestion = In14Days(inputs.HomeHistory);
        var awayCongestion = In14Days(inputs.AwayHistory);

        return new ScheduleSignals
        {
            HomeRestDays = SignalValue.Of(homeRest, homeRest <= 3, $"Home rested {homeRest:F0} days"),
            AwayRestDays = SignalValue.Of(awayRest, awayRest <= 3, $"Away rested {awayRest:F0} days"),
            RestDayGap = SignalValue.Of(gap, Math.Abs(gap) >= opt.RestDayGapFlag,
                $"Rest advantage {gap:+0;-0;0} days for the home side"),
            HomeMatchesLast14Days = SignalValue.Of(homeCongestion, homeCongestion >= opt.CongestionMatchesIn14Days,
                $"Home played {homeCongestion} matches in 14 days"),
            AwayMatchesLast14Days = SignalValue.Of(awayCongestion, awayCongestion >= opt.CongestionMatchesIn14Days,
                $"Away played {awayCongestion} matches in 14 days"),
            HomeTier2Within4Days = SignalValue.Of(inputs.HomeTier2Within4Days ? 1 : 0, inputs.HomeTier2Within4Days,
                inputs.HomeTier2Within4Days ? "Home has a European match within 4 days" : "No European match near for home side"),
            AwayTier2Within4Days = SignalValue.Of(inputs.AwayTier2Within4Days ? 1 : 0, inputs.AwayTier2Within4Days,
                inputs.AwayTier2Within4Days ? "Away has a European match within 4 days" : "No European match near for away side"),
            Travel = SignalValue.Unavailable("Travel distance not computed (no venue geodata)")
        };
    }

    // ── G. Market signals ────────────────────────────────────────────────────

    private static MarketSignals ComputeMarket(
        Fixture f, Team? home, Team? away, PoissonModel? dc, StrategyOptions opt)
    {
        SignalValue Divergence(double? modelP, double marketP, string market)
        {
            if (modelP == null) return SignalValue.Unavailable($"No model probability for {market}");
            var d = Math.Abs(modelP.Value - marketP);
            return SignalValue.Of(d, d >= opt.ModelMarketDivergenceFlag,
                $"Model vs market divergence {d:P0} on {market}");
        }

        // Over 2.5 divergence (Shin pair when both sides exist)
        var divOver = SignalValue.Unavailable("No Over/Under odds");
        if (dc != null && OddsGuard.IsValid(f.Over25Odds) && OddsGuard.IsValid(f.Under25Odds))
        {
            var market = ShinMarginRemovalProxy(f.Over25Odds.Value, f.Under25Odds.Value);
            divOver = Divergence(dc.Over25, market, "Over 2.5");
        }

        var divBtts = SignalValue.Unavailable("No BTTS odds");
        if (dc != null && OddsGuard.IsValid(f.BttsYesOdds))
            divBtts = Divergence(dc.BTTS, 1.0 / f.BttsYesOdds.Value, "BTTS");

        var div1X2 = SignalValue.Unavailable("No 1X2 odds");
        if (dc != null && OddsGuard.IsValid(f.HomeWinOdds) && OddsGuard.IsValid(f.DrawOdds) && OddsGuard.IsValid(f.AwayWinOdds))
        {
            var probs = Services.ShinMarginRemoval.TrueProbabilities(
                [f.HomeWinOdds.Value, f.DrawOdds.Value, f.AwayWinOdds.Value]);
            var d = Math.Max(Math.Abs(dc.HomeWin - probs[0]),
                    Math.Max(Math.Abs(dc.Draw - probs[1]), Math.Abs(dc.AwayWin - probs[2])));
            div1X2 = SignalValue.Of(d, d >= opt.ModelMarketDivergenceFlag,
                $"Max model-market divergence {d:P0} on 1X2");
        }

        // Favorite band
        var favBand = SignalValue.Unavailable("No 1X2 odds");
        SignalValue trap = SignalValue.Unavailable("No 1X2 odds or standings");
        if (OddsGuard.IsValid(f.HomeWinOdds) && OddsGuard.IsValid(f.AwayWinOdds))
        {
            var favIsHome = f.HomeWinOdds.Value <= f.AwayWinOdds.Value;
            var favOdds = Math.Min(f.HomeWinOdds.Value, f.AwayWinOdds.Value);
            var band = favOdds < opt.HeavyFavoriteOdds ? "heavy favorite"
                : favOdds < opt.ModerateFavoriteOdds ? "moderate favorite"
                : favOdds < opt.BalancedFavoriteOdds ? "balanced"
                : "outsider-friendly";
            favBand = SignalValue.Of(favOdds, favOdds < opt.HeavyFavoriteOdds, $"Market: {band} at {favOdds:F2}");

            if (home is { Played: > 0 } && away is { Played: > 0 })
            {
                // Trap pattern: the market favors the clearly WORSE-ranked side.
                var favRank = favIsHome ? home.Rank : away.Rank;
                var dogRank = favIsHome ? away.Rank : home.Rank;
                var againstTable = favRank - dogRank; // positive = favorite ranked worse
                var isTrap = againstTable >= opt.TrapRankGap;
                trap = SignalValue.Of(againstTable, isTrap,
                    isTrap
                        ? $"Market favors the side ranked {againstTable} places WORSE — classic trap pattern"
                        : "Odds aligned with table logic");
            }
        }

        return new MarketSignals
        {
            OpeningDrift = SignalValue.Unavailable("Opening odds not stored — drift unavailable"),
            DivergenceOver25 = divOver,
            DivergenceBtts = divBtts,
            Divergence1X2 = div1X2,
            FavoriteOddsBand = favBand,
            Trap = trap
        };
    }

    // ── H. League profile ────────────────────────────────────────────────────

    private static LeagueProfileSignals ComputeLeague(Fixture f, SignalInputs inputs, StrategyOptions opt)
    {
        var league = inputs.LeagueSeasonMatches;
        if (league.Count == 0)
            return new LeagueProfileSignals
            {
                LeagueOver25Rate = SignalValue.Unavailable("No league matches this season yet"),
                LeagueVolatility = SignalValue.Of(inputs.LeagueVolatility,
                    inputs.LeagueVolatility >= opt.HighVolatilityFlag,
                    $"League volatility {inputs.LeagueVolatility:F2}")
            };

        var leagueOver = (double)league.Count(m => m.HomeGoal + m.AwayGoal > 2) / league.Count;
        var leagueBtts = (double)league.Count(m => m is { HomeGoal: > 0, AwayGoal: > 0 }) / league.Count;

        SignalValue Deviation(IReadOnlyList<Fixture> history, Func<Fixture, bool> pred, double baseRate, string what, string side)
        {
            var window = history.Take(opt.LongWindow).ToList();
            if (window.Count == 0) return SignalValue.Unavailable($"No matches for {side} {what} deviation");
            var teamRate = (double)window.Count(pred) / window.Count;
            var dev = teamRate - baseRate;
            return SignalValue.Of(dev, Math.Abs(dev) >= opt.LeagueDeviationFlag,
                $"{side} {what} rate {teamRate:P0} vs league {baseRate:P0}");
        }

        return new LeagueProfileSignals
        {
            LeagueOver25Rate = SignalValue.Of(leagueOver, leagueOver >= opt.HighRateFlag,
                $"League Over 2.5 base rate {leagueOver:P0} ({league.Count} matches)"),
            LeagueBttsRate = SignalValue.Of(leagueBtts, leagueBtts >= opt.HighRateFlag,
                $"League BTTS base rate {leagueBtts:P0} ({league.Count} matches)"),
            LeagueVolatility = SignalValue.Of(inputs.LeagueVolatility,
                inputs.LeagueVolatility >= opt.HighVolatilityFlag,
                $"League volatility {inputs.LeagueVolatility:F2}"),
            HomeOver25VsLeague = Deviation(inputs.HomeHistory, m => m.HomeGoal + m.AwayGoal > 2, leagueOver, "Over 2.5", "Home"),
            AwayOver25VsLeague = Deviation(inputs.AwayHistory, m => m.HomeGoal + m.AwayGoal > 2, leagueOver, "Over 2.5", "Away"),
            HomeBttsVsLeague = Deviation(inputs.HomeHistory, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, leagueBtts, "BTTS", "Home"),
            AwayBttsVsLeague = Deviation(inputs.AwayHistory, m => m is { HomeGoal: > 0, AwayGoal: > 0 }, leagueBtts, "BTTS", "Away")
        };
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static int GoalsFor(Fixture m, int teamId) => m.HomeTeamId == teamId ? m.HomeGoal : m.AwayGoal;
    private static int GoalsAgainst(Fixture m, int teamId) => m.HomeTeamId == teamId ? m.AwayGoal : m.HomeGoal;
    private static int HtGoalsFor(Fixture m, int teamId) => m.HomeTeamId == teamId ? m.HtHomeGoal : m.HtAwayGoal;
    private static int HtGoalsAgainst(Fixture m, int teamId) => m.HomeTeamId == teamId ? m.HtAwayGoal : m.HtHomeGoal;
    private static int FirstHalfGoalsFor(Fixture m, int teamId) => HtGoalsFor(m, teamId);

    private static int PointsFor(Fixture m, int teamId)
    {
        var scored = GoalsFor(m, teamId);
        var conceded = GoalsAgainst(m, teamId);
        return scored > conceded ? 3 : scored == conceded ? 1 : 0;
    }

    private static char ResultChar(Fixture m, int teamId) =>
        PointsFor(m, teamId) switch { 3 => 'W', 1 => 'D', _ => 'L' };

    /// <summary>Current streak length from the newest match backwards.</summary>
    private static int CountStreak(IReadOnlyList<Fixture> historyNewestFirst, Func<Fixture, bool> predicate)
    {
        var streak = 0;
        foreach (var m in historyNewestFirst)
        {
            if (predicate(m)) streak++;
            else break;
        }
        return streak;
    }

    private static double ShinMarginRemovalProxy(double oddsFor, double oddsAgainst) =>
        Services.ShinMarginRemoval.TrueProbability(oddsFor, oddsAgainst);
}
