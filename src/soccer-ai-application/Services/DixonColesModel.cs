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
                return null;

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
        var fixtures = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                (f.HomeTeamId == teamId || f.AwayTeamId == teamId) &&
                f.Status == "FT" &&
                f.Date < beforeDate)
            .Select(f => new { f.Date, f.HomeTeamId, f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        if (fixtures.Count < _opt.MinTeamMatches)
            return null;

        // Split in memory: scored/conceded from the team's perspective.
        var home = new WeightedGoalStats();
        var away = new WeightedGoalStats();
        var overall = new WeightedGoalStats();

        foreach (var f in fixtures)
        {
            var w = TimeDecayWeight(f.Date, beforeDate);
            var isHome = f.HomeTeamId == teamId;
            var scored = isHome ? f.HomeGoal : f.AwayGoal;
            var conceded = isHome ? f.AwayGoal : f.HomeGoal;

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

        public void Add(double weight, int scored, int conceded)
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
