using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services;

/// <summary>
/// Dixon-Coles Poisson model — the ONLY statistical probability source.
///
/// Data handling:
/// - Uses ALL seasons; older matches are down-weighted by exponential time
///   decay (half-life configurable) instead of an IsCurrentSeason hard cut.
/// - ONE query per team loads its finished fixtures (home+away together);
///   league averages are computed once per (league, cutoff) and cached for
///   the lifetime of this scoped instance (i.e. per request/calculation).
/// - A team short of history in the division being priced is read from its
///   other competitions instead, with the goals converted into this
///   division's scoring environment and discounted. Without that, a promoted
///   or relegated club has no record here at all and the fixture is priced
///   from nothing — which is to say, not priced.
/// - The date filter runs in SQL, not client-side.
///
/// Probability handling:
/// - λ values from time-decayed, venue-blended (70/30), Bayesian-shrunk
///   attack/defense strengths.
/// - All markets come from the same DC-adjusted renormalized score matrix.
/// </summary>
public sealed class DixonColesModel(
    IApplicationDbContext dbContext,
    IOptions<DixonColesOptions> options,
    ILogger<DixonColesModel> logger) : IDixonColesModel
{
    private readonly DixonColesOptions _opt = options.Value;

    // Scoped service ⇒ this cache lives for one request/calculation run.
    private readonly Dictionary<(int LeagueId, DateTimeOffset Cutoff), LeagueAverages> _leagueCache = new();

    public async Task<PoissonProbabilities?> CalculateProbabilitiesAsync(
        int leagueId,
        int homeTeamId,
        int awayTeamId,
        DateTimeOffset matchDate,
        CancellationToken ct = default)
    {
        try
        {
            var leagueAvg = await GetLeagueAveragesAsync(leagueId, matchDate, ct);
            if (leagueAvg.MatchesAnalyzed < _opt.MinLeagueMatches)
            {
                // Say which gate closed. A fixture that reaches the client with
                // no markets at all is otherwise indistinguishable from one the
                // model chose not to back, and the two need different fixes.
                logger.LogInformation(
                    "[DC] League {LeagueId} holds {Count} finished fixtures before {Date:yyyy-MM-dd}, "
                    + "below the minimum of {Min} — nothing in it can be priced",
                    leagueId, leagueAvg.MatchesAnalyzed, matchDate, _opt.MinLeagueMatches);
                return null;
            }

            var homeStats = await GetTeamStrengthAsync(leagueId, homeTeamId, matchDate, leagueAvg, ct);
            var awayStats = await GetTeamStrengthAsync(leagueId, awayTeamId, matchDate, leagueAvg, ct);

            if (homeStats == null || awayStats == null)
                return null;

            // λ = league_avg × attack_strength × opponent_defense_weakness
            var lambdaHome = Math.Clamp(
                leagueAvg.HomeGoalsAvg * homeStats.HomeAttackStrength * awayStats.AwayDefenseWeakness,
                _opt.LambdaMin, _opt.LambdaMax);

            var lambdaAway = Math.Clamp(
                leagueAvg.AwayGoalsAvg * awayStats.AwayAttackStrength * homeStats.HomeDefenseWeakness,
                _opt.LambdaMin, _opt.LambdaMax);

            var matrix = DixonColesMath.BuildScoreMatrix(lambdaHome, lambdaAway, _opt.Rho, _opt.MaxGoals);
            var markets = DixonColesMath.ComputeMarkets(matrix);

            return new PoissonProbabilities
            {
                HomeWin = markets.HomeWin,
                Draw = markets.Draw,
                AwayWin = markets.AwayWin,
                Over25 = markets.Over25,
                BothTeamScoredGoal = markets.Btts,
                TwoToThreeGoals = markets.TwoToThreeGoals,
                BttsAndOver25 = markets.BttsAndOver25,
                HomeExpectedGoals = lambdaHome,
                AwayExpectedGoals = lambdaAway
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dixon-Coles calculation failed for league {LeagueId}", leagueId);
            return null;
        }
    }

    // ── League averages: one SQL query, cached per (league, cutoff) ─────────

    private async Task<LeagueAverages> GetLeagueAveragesAsync(
        int leagueId, DateTimeOffset beforeDate, CancellationToken ct)
    {
        var key = (leagueId, beforeDate);
        if (_leagueCache.TryGetValue(key, out var cached))
            return cached;

        var matches = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                f.Status == "FT" &&
                f.Date < beforeDate)
            .Select(f => new { f.Date, f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        LeagueAverages result;
        if (matches.Count == 0)
        {
            result = new LeagueAverages(leagueId, 0, 0, 0);
        }
        else
        {
            double wSum = 0, homeSum = 0, awaySum = 0;
            foreach (var m in matches)
            {
                var w = TimeDecayWeight(m.Date, beforeDate);
                wSum += w;
                homeSum += w * m.HomeGoal;
                awaySum += w * m.AwayGoal;
            }

            result = new LeagueAverages(
                leagueId,
                Math.Max(_opt.MinLeagueGoalAverage, homeSum / wSum),
                Math.Max(_opt.MinLeagueGoalAverage, awaySum / wSum),
                matches.Count);
        }

        _leagueCache[key] = result;
        return result;
    }

    // ── Team strength: ONE query per team (home + away together) ────────────

    private async Task<TeamSplitStrength?> GetTeamStrengthAsync(
        int leagueId, int teamId, DateTimeOffset beforeDate,
        LeagueAverages leagueAvg, CancellationToken ct)
    {
        // Every competition the team has played, not only this one. Cutting on
        // the target league alone left a promoted or relegated club with no
        // history whatsoever — its record sits under the division it came from
        // — so the model returned null and the fixture reached the client with
        // no probabilities, no markets and no decision audit.
        var fixtures = await dbContext.Fixtures
            .Where(f =>
                (f.HomeTeamId == teamId || f.AwayTeamId == teamId) &&
                f.Status == "FT" &&
                f.Date < beforeDate)
            .Select(f => new { f.Date, f.LeagueId, f.HomeTeamId, f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        var inLeague = fixtures.Where(f => f.LeagueId == leagueId).ToList();

        // Same-division form is used on its own whenever there is enough of it,
        // so an established club is priced exactly as before. The rest of the
        // record only steps in when this division alone cannot price the team.
        var sample = inLeague.Count >= _opt.MinTeamMatches ? inLeague : fixtures;

        if (sample.Count < _opt.MinTeamMatches)
        {
            logger.LogInformation(
                "[DC] Team {TeamId} has {InLeague} finished fixture(s) in league {LeagueId} and "
                + "{Total} across all competitions before {Date:yyyy-MM-dd}, below the minimum of "
                + "{Min} — the fixture cannot be priced",
                teamId, inLeague.Count, leagueId, fixtures.Count, beforeDate, _opt.MinTeamMatches);
            return null;
        }

        // Split in memory: scored/conceded from the team's perspective.
        var home = new WeightedGoalStats();
        var away = new WeightedGoalStats();
        var overall = new WeightedGoalStats();

        var scales = new Dictionary<int, (double Home, double Away)>();

        foreach (var f in sample)
        {
            var w = TimeDecayWeight(f.Date, beforeDate);
            var isHome = f.HomeTeamId == teamId;
            var (homeScale, awayScale) = await GoalScaleAsync(f.LeagueId, leagueId, leagueAvg, beforeDate, scales, ct);

            // A goal is worth what the division it was scored in makes it
            // worth: two goals in a league averaging 1.2 at home say more than
            // two in one averaging 1.7. Conceding is read against the opposite
            // venue's baseline, since that is the side doing the scoring.
            var scored = (isHome ? f.HomeGoal : f.AwayGoal) * (isHome ? homeScale : awayScale);
            var conceded = (isHome ? f.AwayGoal : f.HomeGoal) * (isHome ? awayScale : homeScale);

            if (f.LeagueId != leagueId) w *= _opt.CrossLeagueWeight;

            overall.Add(w, scored, conceded);
            if (isHome) home.Add(w, scored, conceded);
            else away.Add(w, scored, conceded);
        }

        // Venue averages with league fallback when a venue has no sample yet.
        var homeScoredVenue = home.HasData ? home.ScoredAvg : leagueAvg.HomeGoalsAvg;
        var homeConcededVenue = home.HasData ? home.ConcededAvg : leagueAvg.AwayGoalsAvg;
        var awayScoredVenue = away.HasData ? away.ScoredAvg : leagueAvg.AwayGoalsAvg;
        var awayConcededVenue = away.HasData ? away.ConcededAvg : leagueAvg.HomeGoalsAvg;

        // Venue blending: VenueWeight venue + (1−VenueWeight) overall.
        var overallWeight = 1 - _opt.VenueWeight;
        var homeScored = homeScoredVenue * _opt.VenueWeight + overall.ScoredAvg * overallWeight;
        var homeConceded = homeConcededVenue * _opt.VenueWeight + overall.ConcededAvg * overallWeight;
        var awayScored = awayScoredVenue * _opt.VenueWeight + overall.ScoredAvg * overallWeight;
        var awayConceded = awayConcededVenue * _opt.VenueWeight + overall.ConcededAvg * overallWeight;

        // Bayesian shrinkage toward league average using the EFFECTIVE
        // (decay-weighted) sample size, so stale data both counts less and
        // shrinks harder.
        homeScored = BayesianAdjust(homeScored, leagueAvg.HomeGoalsAvg, home.EffectiveCount);
        homeConceded = BayesianAdjust(homeConceded, leagueAvg.AwayGoalsAvg, home.EffectiveCount);
        awayScored = BayesianAdjust(awayScored, leagueAvg.AwayGoalsAvg, away.EffectiveCount);
        awayConceded = BayesianAdjust(awayConceded, leagueAvg.HomeGoalsAvg, away.EffectiveCount);

        return new TeamSplitStrength
        {
            HomeAttackStrength = homeScored / leagueAvg.HomeGoalsAvg,
            HomeDefenseWeakness = homeConceded / leagueAvg.AwayGoalsAvg,
            AwayAttackStrength = awayScored / leagueAvg.AwayGoalsAvg,
            AwayDefenseWeakness = awayConceded / leagueAvg.HomeGoalsAvg
        };
    }

    /// <summary>
    /// Factors that convert goals scored in <paramref name="sourceLeagueId"/>
    /// into the scoring environment of <paramref name="targetLeagueId"/>.
    /// </summary>
    /// <remarks>
    /// (1, 1) for the target division itself, and also for a division we hold
    /// too little of: a baseline built on a handful of matches is noise, and
    /// rescaling by noise is worse than leaving the goals as they are.
    /// </remarks>
    private async Task<(double Home, double Away)> GoalScaleAsync(
        int sourceLeagueId, int targetLeagueId, LeagueAverages targetAvg,
        DateTimeOffset beforeDate, Dictionary<int, (double Home, double Away)> cache,
        CancellationToken ct)
    {
        if (sourceLeagueId == targetLeagueId) return (1, 1);
        if (cache.TryGetValue(sourceLeagueId, out var cached)) return cached;

        var source = await GetLeagueAveragesAsync(sourceLeagueId, beforeDate, ct);
        var scale = source.MatchesAnalyzed >= _opt.MinLeagueMatches
            ? (targetAvg.HomeGoalsAvg / source.HomeGoalsAvg, targetAvg.AwayGoalsAvg / source.AwayGoalsAvg)
            : (1d, 1d);

        cache[sourceLeagueId] = scale;
        return scale;
    }

    // ── Weighting helpers ────────────────────────────────────────────────────

    /// <summary>w = 0.5^(ageDays / halfLife); future-dated safety-clamped to 1.</summary>
    private double TimeDecayWeight(DateTimeOffset matchDate, DateTimeOffset reference)
    {
        var ageDays = Math.Max(0, (reference - matchDate).TotalDays);
        return Math.Pow(0.5, ageDays / _opt.DecayHalfLifeDays);
    }

    private double BayesianAdjust(double teamAvg, double leagueAvg, double effectiveMatches) =>
        (teamAvg * effectiveMatches + leagueAvg * _opt.BayesianPriorStrength)
        / (effectiveMatches + _opt.BayesianPriorStrength);

    private sealed class WeightedGoalStats
    {
        private double _wSum, _scoredSum, _concededSum;

        public void Add(double weight, double scored, double conceded)
        {
            _wSum += weight;
            _scoredSum += weight * scored;
            _concededSum += weight * conceded;
        }

        public bool HasData => _wSum > 0;
        public double EffectiveCount => _wSum;
        public double ScoredAvg => _scoredSum / _wSum;
        public double ConcededAvg => _concededSum / _wSum;
    }
}

public sealed class TeamSplitStrength
{
    public double HomeAttackStrength { get; init; }
    public double HomeDefenseWeakness { get; init; }
    public double AwayAttackStrength { get; init; }
    public double AwayDefenseWeakness { get; init; }
}
