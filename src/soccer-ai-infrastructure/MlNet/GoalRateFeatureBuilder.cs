using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Forecasts;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>
/// Builds <see cref="GoalRateRow"/>s by walking fixtures in kickoff order and
/// maintaining team history incrementally.
///
/// Anti-leakage rule, and the reason this class exists rather than a set of
/// SQL window functions: a row is emitted from the state as it stands BEFORE
/// the fixture, and only then is the fixture folded into that state. Nothing a
/// row can see happened after its own kickoff.
///
/// Dixon-Coles is recomputed here incrementally rather than by calling
/// <c>IDixonColesModel</c> per fixture. That service issues two database
/// queries per call, which over ~24,000 fixtures is tens of thousands of
/// round trips; the decayed accumulators below produce the same λ pair in one
/// pass. The maths is shared — both end at
/// <see cref="DixonColesMath.BuildScoreMatrix"/>.
/// </summary>
public sealed class GoalRateFeatureBuilder(
    IOptions<DixonColesOptions> dixonColesOptions,
    ILogger<GoalRateFeatureBuilder> logger)
{
    private readonly DixonColesOptions _dc = dixonColesOptions.Value;

    private const int LongWindow = 10;
    private const int ShortWindow = 5;
    private const int H2HWindow = 6;
    private const float DefaultRestDays = 7f;
    private const float MaxRestDays = 14f;
    private const double DefaultElo = 1500.0;
    public const string SchemaVersion = "goal-rate-causal-v2";
    public DixonColesOptions DixonColesSettings => _dc;

    /// <summary>
    /// Emits one row per fixture, in kickoff order.
    /// </summary>
    /// <param name="fixtures">
    /// Finished and unplayed fixtures together. Only finished ones update the
    /// rolling state, so an unplayed fixture is scored from exactly the history
    /// that exists before it — the same code path that produced the training
    /// rows. Passing upcoming fixtures alongside their history is how live
    /// prediction reuses this builder instead of a parallel implementation
    /// that could silently drift from it.
    /// </param>
    public IReadOnlyList<GoalRateRow> Build(IEnumerable<Fixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var ordered = fixtures
            .OrderBy(f => f.Date)
            .ThenBy(f => f.Id)
            .ToList();

        logger.LogInformation("[GoalRate] Building features for {Count} fixtures", ordered.Count);

        var leagueHome = new Dictionary<int, Decayed>();
        var leagueAway = new Dictionary<int, Decayed>();
        var leagueCount = new Dictionary<int, int>();
        var teamHome = new Dictionary<int, Decayed>();
        var teamAway = new Dictionary<int, Decayed>();
        var teamAll = new Dictionary<int, Decayed>();
        var teamCount = new Dictionary<int, int>();

        var form = new Dictionary<int, TeamForm>();
        var venue = new Dictionary<(int Team, bool AtHome), VenueForm>();
        var h2h = new Dictionary<(int, int), H2HForm>();
        var lastPlayed = new Dictionary<int, DateTimeOffset>();

        var elo = new Dictionary<int, double>();
        var rows = new List<GoalRateRow>(ordered.Count);

        // No final-whistle timestamps exist in the historical store. Freeze
        // history for the whole UTC day, so simultaneous/unfinished games can
        // never supply results to another pre-match row.
        foreach (var day in ordered.GroupBy(f => f.Date.UtcDateTime.Date))
        {
        foreach (var f in day)
        {
            var lgHome = Get(leagueHome, f.LeagueId);
            var lgAway = Get(leagueAway, f.LeagueId);
            var lgHomeAvg = Math.Max(lgHome.ScoredAvg(f.Date), _dc.MinLeagueGoalAverage);
            var lgAwayAvg = Math.Max(lgAway.ScoredAvg(f.Date), _dc.MinLeagueGoalAverage);

            var isFinished = f.Status == "FT";

            var row = new GoalRateRow
            {
                FixtureId = f.Id,
                LeagueId = f.LeagueId,
                Date = f.Date.UtcDateTime,
                IsFinished = isFinished,
                // Goals on an unplayed fixture are 0 by default, not 0-0. The
                // flag above is what training filters on so those never become
                // labels.
                GoalsHome = isFinished ? f.HomeGoal : 0,
                GoalsAway = isFinished ? f.AwayGoal : 0,
                LeagueHomeAvg = (float)lgHomeAvg,
                LeagueAwayAvg = (float)lgAwayAvg,
                IsDerby = f.IsDerby ? 1f : 0f,
                EloDiff = (float)(elo.GetValueOrDefault(f.HomeTeamId, DefaultElo)
                    - elo.GetValueOrDefault(f.AwayTeamId, DefaultElo)),
                HomeRestDays = Rest(lastPlayed, f.HomeTeamId, f.Date),
                AwayRestDays = Rest(lastPlayed, f.AwayTeamId, f.Date),
                HomeHistory = Count(teamCount, f.HomeTeamId),
                AwayHistory = Count(teamCount, f.AwayTeamId),
            };

            ApplyDixonColes(row, f, lgHomeAvg, lgAwayAvg,
                leagueCount, teamCount, teamHome, teamAway, teamAll);
            ApplyMarket(row, f);
            ApplyRollingForm(row, f, form, venue, h2h);

            rows.Add(row);

        }
            foreach (var f in day.Where(f => f.Status == "FT"))
            {
                Advance(f, leagueHome, leagueAway, leagueCount, teamHome, teamAway, teamAll,
                    teamCount, form, venue, h2h, lastPlayed);
                var homeElo = elo.GetValueOrDefault(f.HomeTeamId, DefaultElo);
                var awayElo = elo.GetValueOrDefault(f.AwayTeamId, DefaultElo);
                var expectedHome = 1 / (1 + Math.Pow(10, (awayElo - homeElo - 65) / 400));
                var actualHome = f.HomeGoal > f.AwayGoal ? 1.0 : f.HomeGoal == f.AwayGoal ? 0.5 : 0;
                var change = 20 * (actualHome - expectedHome);
                elo[f.HomeTeamId] = homeElo + change;
                elo[f.AwayTeamId] = awayElo - change;
            }
        }

        logger.LogInformation(
            "[GoalRate] Built {Rows} rows ({Finished} labelled); "
            + "Dixon-Coles available on {Dc}, market λ on {Mkt}",
            rows.Count, rows.Count(r => r.IsFinished),
            rows.Count(r => r.DcLambdaSum > 0), rows.Count(r => r.HasMktLambda > 0));

        return rows;
    }

    // ── Dixon-Coles ─────────────────────────────────────────────────────────

    private void ApplyDixonColes(
        GoalRateRow row, Fixture f, double lgHomeAvg, double lgAwayAvg,
        Dictionary<int, int> leagueCount, Dictionary<int, int> teamCount,
        Dictionary<int, Decayed> teamHome, Dictionary<int, Decayed> teamAway,
        Dictionary<int, Decayed> teamAll)
    {
        if (leagueCount.GetValueOrDefault(f.LeagueId) < _dc.MinLeagueMatches ||
            teamCount.GetValueOrDefault(f.HomeTeamId) < _dc.MinTeamMatches ||
            teamCount.GetValueOrDefault(f.AwayTeamId) < _dc.MinTeamMatches)
        {
            // Left at zero. The trainer sees an all-zero DC block and learns to
            // lean on the market and form features for these fixtures, which is
            // exactly what a human would do with no usable history.
            return;
        }

        var (hScored, hConceded) = Strength(
            Get(teamHome, f.HomeTeamId), Get(teamAll, f.HomeTeamId), f.Date, lgHomeAvg, lgAwayAvg);
        var (aScored, aConceded) = Strength(
            Get(teamAway, f.AwayTeamId), Get(teamAll, f.AwayTeamId), f.Date, lgAwayAvg, lgHomeAvg);

        var lambdaHome = Math.Clamp(
            lgHomeAvg * (hScored / lgHomeAvg) * (aConceded / lgHomeAvg),
            _dc.LambdaMin, _dc.LambdaMax);
        var lambdaAway = Math.Clamp(
            lgAwayAvg * (aScored / lgAwayAvg) * (hConceded / lgAwayAvg),
            _dc.LambdaMin, _dc.LambdaMax);

        var matrix = DixonColesMath.BuildScoreMatrix(lambdaHome, lambdaAway, _dc.Rho, _dc.MaxGoals);
        var m = DixonColesMath.ComputeMarkets(matrix);

        row.DcOver25 = (float)m.Over25;
        row.DcBtts = (float)m.Btts;
        row.DcHome = (float)m.HomeWin;
        row.DcDraw = (float)m.Draw;
        row.DcAway = (float)m.AwayWin;
        row.DcLambdaHome = (float)lambdaHome;
        row.DcLambdaAway = (float)lambdaAway;
        row.DcLambdaSum = (float)(lambdaHome + lambdaAway);
        row.DcLambdaMin = (float)Math.Min(lambdaHome, lambdaAway);
    }

    /// <summary>
    /// Venue-blended, Bayesian-shrunk scored/conceded rates, mirroring
    /// <c>DixonColesModel.GetTeamStrengthAsync</c>.
    /// </summary>
    private (double Scored, double Conceded) Strength(
        Decayed venueState, Decayed overall, DateTimeOffset asOf,
        double leagueFor, double leagueAgainst)
    {
        var vWeight = venueState.Weight(asOf);
        var vScored = vWeight > 0 ? venueState.ScoredAvg(asOf) : leagueFor;
        var vConceded = vWeight > 0 ? venueState.ConcededAvg(asOf) : leagueAgainst;

        var oWeight = 1 - _dc.VenueWeight;
        var scored = vScored * _dc.VenueWeight + overall.ScoredAvg(asOf) * oWeight;
        var conceded = vConceded * _dc.VenueWeight + overall.ConcededAvg(asOf) * oWeight;

        scored = (scored * vWeight + leagueFor * _dc.BayesianPriorStrength)
                 / (vWeight + _dc.BayesianPriorStrength);
        conceded = (conceded * vWeight + leagueAgainst * _dc.BayesianPriorStrength)
                   / (vWeight + _dc.BayesianPriorStrength);

        return (scored, conceded);
    }

    // ── Market ──────────────────────────────────────────────────────────────

    private static void ApplyMarket(GoalRateRow row, Fixture f)
    {
        // Untimestamped imported/closing prices cannot be represented as known
        // before the prediction. New snapshots establish that provenance.
        if (f.OddsCheckedAtUtc is not { } captured || f.OddsUpdatedAtUtc is not { } updated ||
            captured >= f.Date || captured > DateTimeOffset.UtcNow || updated > captured ||
            captured - updated > TimeSpan.FromHours(3)) return;
        if (OddsGuard.IsValid(f.Over25Odds) && OddsGuard.IsValid(f.Under25Odds))
        {
            row.MktOver25 = (float)ShinMarginRemoval.TrueProbability(
                f.Over25Odds!.Value, f.Under25Odds!.Value);
            row.HasMktOver = 1f;
        }
        else if (OddsGuard.IsValid(f.Over25Odds))
        {
            row.MktOver25 = (float)(1.0 / f.Over25Odds!.Value);
            row.HasMktOver = 1f;
        }

        if (OddsGuard.IsValid(f.BttsYesOdds))
        {
            row.MktBtts = (float)(1.0 / f.BttsYesOdds!.Value);
            row.HasMktBtts = 1f;
        }

        if (OddsGuard.IsValid(f.HomeWinOdds) && OddsGuard.IsValid(f.DrawOdds) &&
            OddsGuard.IsValid(f.AwayWinOdds))
        {
            var fair = ShinMarginRemoval.TrueProbabilities(
                [f.HomeWinOdds!.Value, f.DrawOdds!.Value, f.AwayWinOdds!.Value]);
            row.MktFavourite = (float)Math.Max(fair[0], fair[2]);
        }

        var solved = MarketLambdaSolver.Solve(
            f.HomeWinOdds, f.DrawOdds, f.AwayWinOdds, f.Over25Odds, f.Under25Odds);

        if (solved is { } s)
        {
            row.MktLambdaHome = (float)s.LambdaHome;
            row.MktLambdaAway = (float)s.LambdaAway;
            row.MktLambdaSum = (float)(s.LambdaHome + s.LambdaAway);
            row.MktLambdaMin = (float)Math.Min(s.LambdaHome, s.LambdaAway);
            row.MktBttsImplied = (float)s.Btts;
            row.MktOverImplied = (float)s.Over25;
            row.HasMktLambda = 1f;
        }

        // Divergences are only meaningful when both sides of the comparison are
        // real; a difference against a zero placeholder is not a disagreement.
        if (row.HasMktOver > 0 && row.DcOver25 > 0) row.DivOver = row.DcOver25 - row.MktOver25;
        if (row.HasMktBtts > 0 && row.DcBtts > 0) row.DivBtts = row.DcBtts - row.MktBtts;
        if (row.HasMktLambda > 0 && row.DcLambdaSum > 0)
        {
            row.DivLambdaSum = row.DcLambdaSum - row.MktLambdaSum;
            row.DivBttsImplied = row.DcBtts - row.MktBttsImplied;
        }
    }

    // ── Rolling form ────────────────────────────────────────────────────────

    private void ApplyRollingForm(
        GoalRateRow row, Fixture f,
        Dictionary<int, TeamForm> form,
        Dictionary<(int, bool), VenueForm> venue,
        Dictionary<(int, int), H2HForm> h2h)
    {
        var h = Get(form, f.HomeTeamId);
        var a = Get(form, f.AwayTeamId);

        row.HomeXgFor = h.XgFor.Mean(); row.HomeXgAgainst = h.XgAgainst.Mean();
        row.AwayXgFor = a.XgFor.Mean(); row.AwayXgAgainst = a.XgAgainst.Mean();
        row.HomeXgSamples = h.XgFor.Count; row.AwayXgSamples = a.XgFor.Count;
        row.HomeXgAgainstSamples = h.XgAgainst.Count; row.AwayXgAgainstSamples = a.XgAgainst.Count;
        row.HomeSotFor = h.SotFor.Mean(); row.HomeSotAgainst = h.SotAgainst.Mean();
        row.AwaySotFor = a.SotFor.Mean(); row.AwaySotAgainst = a.SotAgainst.Mean();
        row.HomeShotsFor = h.ShotsFor.Mean(); row.AwayShotsFor = a.ShotsFor.Mean();
        row.HomeGoalsFor = h.GoalsFor.Mean(); row.HomeGoalsAgainst = h.GoalsAgainst.Mean();
        row.AwayGoalsFor = a.GoalsFor.Mean(); row.AwayGoalsAgainst = a.GoalsAgainst.Mean();
        row.HomePossession = h.Possession.Mean(); row.AwayPossession = a.Possession.Mean();
        row.HomeBttsRate = h.Btts.Mean(); row.AwayBttsRate = a.Btts.Mean();
        row.HomeOver25Rate = h.Over25.Mean(); row.AwayOver25Rate = a.Over25.Mean();
        row.HomeMatchTotal = h.MatchTotal.Mean(); row.AwayMatchTotal = a.MatchTotal.Mean();
        row.HomeHalfTimeTotal = h.HalfTimeTotal.Mean(); row.AwayHalfTimeTotal = a.HalfTimeTotal.Mean();
        row.HomeGoalsForShort = h.GoalsForShort.Mean(); row.HomeGoalsAgainstShort = h.GoalsAgainstShort.Mean();
        row.AwayGoalsForShort = a.GoalsForShort.Mean(); row.AwayGoalsAgainstShort = a.GoalsAgainstShort.Mean();
        row.HomeXgForShort = h.XgForShort.Mean(); row.AwayXgForShort = a.XgForShort.Mean();

        var hv = Get(venue, (f.HomeTeamId, true));
        var av = Get(venue, (f.AwayTeamId, false));
        row.HomeVenueXgFor = hv.XgFor.Mean(); row.HomeVenueXgAgainst = hv.XgAgainst.Mean();
        row.AwayVenueXgFor = av.XgFor.Mean(); row.AwayVenueXgAgainst = av.XgAgainst.Mean();
        row.HomeVenueGoalsFor = hv.GoalsFor.Mean(); row.HomeVenueGoalsAgainst = hv.GoalsAgainst.Mean();
        row.AwayVenueGoalsFor = av.GoalsFor.Mean(); row.AwayVenueGoalsAgainst = av.GoalsAgainst.Mean();
        row.HomeVenueBtts = hv.Btts.Mean(); row.AwayVenueBtts = av.Btts.Mean();
        row.HomeVenueOver25 = hv.Over25.Mean(); row.AwayVenueOver25 = av.Over25.Mean();

        var hh = Get(h2h, H2HKey(f.HomeTeamId, f.AwayTeamId));
        row.H2HBtts = hh.Btts.Mean(); row.H2HOver25 = hh.Over25.Mean();
        row.H2HTotal = hh.MatchTotal.Mean(); row.H2HSample = hh.MatchTotal.Count;
    }

    private void Advance(
        Fixture f,
        Dictionary<int, Decayed> leagueHome, Dictionary<int, Decayed> leagueAway,
        Dictionary<int, int> leagueCount,
        Dictionary<int, Decayed> teamHome, Dictionary<int, Decayed> teamAway,
        Dictionary<int, Decayed> teamAll, Dictionary<int, int> teamCount,
        Dictionary<int, TeamForm> form, Dictionary<(int, bool), VenueForm> venue,
        Dictionary<(int, int), H2HForm> h2h, Dictionary<int, DateTimeOffset> lastPlayed)
    {
        double hg = f.HomeGoal, ag = f.AwayGoal;

        Get(leagueHome, f.LeagueId).Add(f.Date, hg, ag);
        Get(leagueAway, f.LeagueId).Add(f.Date, ag, hg);
        leagueCount[f.LeagueId] = leagueCount.GetValueOrDefault(f.LeagueId) + 1;

        Get(teamHome, f.HomeTeamId).Add(f.Date, hg, ag);
        Get(teamAway, f.AwayTeamId).Add(f.Date, ag, hg);
        Get(teamAll, f.HomeTeamId).Add(f.Date, hg, ag);
        Get(teamAll, f.AwayTeamId).Add(f.Date, ag, hg);
        teamCount[f.HomeTeamId] = teamCount.GetValueOrDefault(f.HomeTeamId) + 1;
        teamCount[f.AwayTeamId] = teamCount.GetValueOrDefault(f.AwayTeamId) + 1;

        var btts = hg > 0 && ag > 0 ? 1f : 0f;
        var over = hg + ag > 2 ? 1f : 0f;
        var total = (float)(hg + ag);
        var htTotal = f.HtHomeGoal + f.HtAwayGoal;

        // Legacy HomeXg mixes observations with synthesized averages. Use only
        // explicit provider measurements, preserving legitimate zero readings.
        float? hXg = f.HomeObservedXg is { } hx && double.IsFinite(hx) && hx >= 0 ? (float)hx : null;
        float? aXg = f.AwayObservedXg is { } ax && double.IsFinite(ax) && ax >= 0 ? (float)ax : null;

        Get(form, f.HomeTeamId).Add(hg, ag, hXg, aXg, f.HomeShots, f.AwayShots,
            f.HomeShotsOnTarget, f.AwayShotsOnTarget, f.HomeBallPossession, btts, over, total, htTotal);
        Get(form, f.AwayTeamId).Add(ag, hg, aXg, hXg, f.AwayShots, f.HomeShots,
            f.AwayShotsOnTarget, f.HomeShotsOnTarget, f.AwayBallPossession, btts, over, total, htTotal);

        Get(venue, (f.HomeTeamId, true)).Add(hg, ag, hXg, aXg, btts, over);
        Get(venue, (f.AwayTeamId, false)).Add(ag, hg, aXg, hXg, btts, over);

        Get(h2h, H2HKey(f.HomeTeamId, f.AwayTeamId)).Add(btts, over, total);

        lastPlayed[f.HomeTeamId] = f.Date;
        lastPlayed[f.AwayTeamId] = f.Date;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (int, int) H2HKey(int a, int b) => a < b ? (a, b) : (b, a);

    private static float Rest(
        Dictionary<int, DateTimeOffset> last, int teamId, DateTimeOffset now) =>
        last.TryGetValue(teamId, out var prev)
            ? Math.Clamp((float)(now - prev).TotalDays, 0f, MaxRestDays)
            : DefaultRestDays;

    private static float Count(Dictionary<int, int> counts, int teamId) =>
        counts.GetValueOrDefault(teamId);

    private TValue Get<TKey, TValue>(Dictionary<TKey, TValue> map, TKey key)
        where TKey : notnull where TValue : new()
    {
        if (map.TryGetValue(key, out var v)) return v;
        var created = new TValue();
        if (created is Decayed state) state.HalfLifeDays = _dc.DecayHalfLifeDays;
        return map[key] = created;
    }

    /// <summary>
    /// Exponentially time-decayed scored/conceded totals. Weights are aged
    /// lazily on read, so a team that stops playing decays correctly without
    /// anyone having to tick it forward.
    /// </summary>
    private sealed class Decayed
    {
        private double _weight, _scored, _conceded;
        private DateTimeOffset? _asOf;

        private void Age(DateTimeOffset now)
        {
            if (_asOf is null) { _asOf = now; return; }
            var days = (now - _asOf.Value).TotalDays;
            if (days <= 0) return;
            var factor = Math.Pow(0.5, days / HalfLifeDays);
            _weight *= factor;
            _scored *= factor;
            _conceded *= factor;
            _asOf = now;
        }

        public double HalfLifeDays { get; set; } = 180;

        public void Add(DateTimeOffset now, double scored, double conceded)
        {
            Age(now);
            _weight += 1;
            _scored += scored;
            _conceded += conceded;
        }

        public double Weight(DateTimeOffset now) { Age(now); return _weight; }
        public double ScoredAvg(DateTimeOffset now) { Age(now); return _weight > 0 ? _scored / _weight : 0; }
        public double ConcededAvg(DateTimeOffset now) { Age(now); return _weight > 0 ? _conceded / _weight : 0; }
    }

    private sealed class TeamForm
    {
        public readonly Window GoalsFor = new(LongWindow), GoalsAgainst = new(LongWindow);
        public readonly Window XgFor = new(LongWindow), XgAgainst = new(LongWindow);
        public readonly Window ShotsFor = new(LongWindow), ShotsAgainst = new(LongWindow);
        public readonly Window SotFor = new(LongWindow), SotAgainst = new(LongWindow);
        public readonly Window Possession = new(LongWindow);
        public readonly Window Btts = new(LongWindow), Over25 = new(LongWindow);
        public readonly Window MatchTotal = new(LongWindow), HalfTimeTotal = new(LongWindow);
        public readonly Window GoalsForShort = new(ShortWindow), GoalsAgainstShort = new(ShortWindow);
        public readonly Window XgForShort = new(ShortWindow);

        public void Add(double gf, double ga, float? xgFor, float? xgAgainst,
            int shotsFor, int shotsAgainst, int sotFor, int sotAgainst,
            int? possession, float btts, float over, float total, float htTotal)
        {
            GoalsFor.Add((float)gf); GoalsAgainst.Add((float)ga);
            GoalsForShort.Add((float)gf); GoalsAgainstShort.Add((float)ga);
            XgFor.Add(xgFor); XgAgainst.Add(xgAgainst); XgForShort.Add(xgFor);
            ShotsFor.Add(shotsFor > 0 ? shotsFor : null);
            ShotsAgainst.Add(shotsAgainst > 0 ? shotsAgainst : null);
            SotFor.Add(sotFor > 0 ? sotFor : null);
            SotAgainst.Add(sotAgainst > 0 ? sotAgainst : null);
            Possession.Add(possession);
            Btts.Add(btts); Over25.Add(over);
            MatchTotal.Add(total); HalfTimeTotal.Add(htTotal);
        }
    }

    private sealed class VenueForm
    {
        public readonly Window GoalsFor = new(ShortWindow), GoalsAgainst = new(ShortWindow);
        public readonly Window XgFor = new(ShortWindow), XgAgainst = new(ShortWindow);
        public readonly Window Btts = new(ShortWindow), Over25 = new(ShortWindow);

        public void Add(double gf, double ga, float? xgFor, float? xgAgainst, float btts, float over)
        {
            GoalsFor.Add((float)gf); GoalsAgainst.Add((float)ga);
            XgFor.Add(xgFor); XgAgainst.Add(xgAgainst);
            Btts.Add(btts); Over25.Add(over);
        }
    }

    private sealed class H2HForm
    {
        public readonly Window Btts = new(H2HWindow), Over25 = new(H2HWindow), MatchTotal = new(H2HWindow);

        public void Add(float btts, float over, float total)
        {
            Btts.Add(btts); Over25.Add(over); MatchTotal.Add(total);
        }
    }

    /// <summary>
    /// Fixed-size rolling mean. Nulls are skipped rather than counted as zero:
    /// a missing xG reading is unknown, and averaging it in as 0.0 would
    /// invent a goalless performance.
    /// </summary>
    private sealed class Window(int capacity)
    {
        private readonly Queue<float?> _values = new(capacity);
        private int _observed;
        private float _sum;

        public void Add(float? value)
        {
            if (_values.Count == capacity && _values.Dequeue() is { } old)
            {
                _sum -= old;
                _observed--;
            }
            var finite = value is { } v && float.IsFinite(v) ? value : null;
            _values.Enqueue(finite);
            if (finite is { } next) { _sum += next; _observed++; }
        }

        public float Count => _observed;

        /// <summary>Mean, or 0 when the window is empty (trees split on it fine).</summary>
        public float Mean() => _observed == 0 ? 0f : _sum / _observed;
    }
}
