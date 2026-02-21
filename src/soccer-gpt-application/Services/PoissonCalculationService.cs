using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

/// <summary>
/// Professional Poisson probability calculator with Dixon-Coles adjustment
/// Uses bookmaker-style goal expectation modeling
/// </summary>
public class PoissonCalculationService(
    IApplicationDbContext dbContext,
    ILogger<PoissonCalculationService> logger) : IPoissonCalculationService
{
    private const int MinMatchesForCalculation = 10;
    private const double DixonColesRho = -0.13;
    private const double RecencyDecay = 0.90;
    private const int BayesianPriorStrength = 10;
    private const double VenueWeight = 0.70;
    private const double OverallWeight = 0.30;

    public async Task<PoissonProbabilities?> CalculateProbabilitiesAsync(
        int leagueId,
        int homeTeamId,
        int awayTeamId,
        DateTime matchDate,
        CancellationToken ct = default)
    {
        try
        {
            var leagueAvg = await GetLeagueAveragesAsync(leagueId, matchDate, ct);

            if (leagueAvg.MatchesAnalyzed < MinMatchesForCalculation)
                return null;

            var homeStats = await GetTeamSplitStrengthAsync(leagueId, homeTeamId, matchDate, ct);
            var awayStats = await GetTeamSplitStrengthAsync(leagueId, awayTeamId, matchDate, ct);

            if (homeStats == null || awayStats == null)
                return null;

            // ===== BOOKMAKER MODEL =====
            // λ = league_avg × attack_strength × opponent_defense

            var lambdaHome =
                leagueAvg.HomeGoalsAvg *
                homeStats.HomeAttackStrength *
                awayStats.AwayDefenseWeakness;

            var lambdaAway =
                leagueAvg.AwayGoalsAvg *
                awayStats.AwayAttackStrength *
                homeStats.HomeDefenseWeakness;

            lambdaHome = Math.Clamp(lambdaHome, 0.2, 4.5);
            lambdaAway = Math.Clamp(lambdaAway, 0.2, 4.5);

            var outcomes = CalculateOutcomes(lambdaHome, lambdaAway);

            return new PoissonProbabilities
            {
                HomeWin = outcomes.homeWin,
                Draw = outcomes.draw,
                AwayWin = outcomes.awayWin,
                Over25 = outcomes.over25,
                BothTeamScoredGoal = outcomes.btts,
                TwoToThreeGoals = outcomes.goals23,
                HomeExpectedGoals = lambdaHome,
                AwayExpectedGoals = lambdaAway
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Poisson calculation failed");
            return null;
        }
    }

    // =============================
    // LEAGUE AVERAGES
    // =============================

    private async Task<LeagueAverages> GetLeagueAveragesAsync(
        int leagueId,
        DateTime beforeDate,
        CancellationToken ct)
    {
        var matches = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                f.Status == "FT" &&
                f.IsCurrentSeason &&
                f.Date < beforeDate)
            .Select(f => new { f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return new LeagueAverages(leagueId, 0.5, 0.5, 0);

        return new LeagueAverages(
            leagueId,
            matches.Average(m => m.HomeGoal),
            matches.Average(m => m.AwayGoal),
            matches.Count);
    }

    // =============================
    // TEAM SPLIT STRENGTH (HOME/AWAY)
    // =============================

    private async Task<TeamSplitStrength?> GetTeamSplitStrengthAsync(
        int leagueId,
        int teamId,
        DateTime beforeDate,
        CancellationToken ct)
    {
        var leagueAvg = await GetLeagueAveragesAsync(leagueId, beforeDate, ct);
        if (leagueAvg.MatchesAnalyzed < MinMatchesForCalculation)
            return null;

        // Fetch with dates for recency weighting (most recent first)
        var homeMatches = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                f.HomeTeamId == teamId &&
                f.Status == "FT" &&
                f.IsCurrentSeason &&
                f.Date < beforeDate)
            .OrderByDescending(f => f.Date)
            .Select(f => new { f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        var awayMatches = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                f.AwayTeamId == teamId &&
                f.Status == "FT" &&
                f.IsCurrentSeason &&
                f.Date < beforeDate)
            .OrderByDescending(f => f.Date)
            .Select(f => new { f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        // Also fetch ALL matches (both home + away) for overall form (venue blending)
        var allMatches = await dbContext.Fixtures
            .Where(f =>
                f.LeagueId == leagueId &&
                (f.HomeTeamId == teamId || f.AwayTeamId == teamId) &&
                f.Status == "FT" &&
                f.IsCurrentSeason &&
                f.Date < beforeDate)
            .OrderByDescending(f => f.Date)
            .Select(f => new { f.HomeTeamId, f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        if (homeMatches.Count + awayMatches.Count < 3)
            return null;

        // ── Recency-weighted venue stats ──
        var homeScoredVenue = homeMatches.Any()
            ? RecencyWeightedAverage(homeMatches.Select(x => (double)x.HomeGoal).ToList())
            : leagueAvg.HomeGoalsAvg;
        var homeConcededVenue = homeMatches.Any()
            ? RecencyWeightedAverage(homeMatches.Select(x => (double)x.AwayGoal).ToList())
            : leagueAvg.AwayGoalsAvg;

        var awayScoredVenue = awayMatches.Any()
            ? RecencyWeightedAverage(awayMatches.Select(x => (double)x.AwayGoal).ToList())
            : leagueAvg.AwayGoalsAvg;
        var awayConcededVenue = awayMatches.Any()
            ? RecencyWeightedAverage(awayMatches.Select(x => (double)x.HomeGoal).ToList())
            : leagueAvg.HomeGoalsAvg;

        // ── Recency-weighted overall stats (for venue blending) ──
        var overallScored = RecencyWeightedAverage(
            allMatches.Select(x => (double)(x.HomeTeamId == teamId ? x.HomeGoal : x.AwayGoal)).ToList());
        var overallConceded = RecencyWeightedAverage(
            allMatches.Select(x => (double)(x.HomeTeamId == teamId ? x.AwayGoal : x.HomeGoal)).ToList());

        // ── Venue blending: 70% venue + 30% overall ──
        var homeScored = homeScoredVenue * VenueWeight + overallScored * OverallWeight;
        var homeConceded = homeConcededVenue * VenueWeight + overallConceded * OverallWeight;
        var awayScored = awayScoredVenue * VenueWeight + overallScored * OverallWeight;
        var awayConceded = awayConcededVenue * VenueWeight + overallConceded * OverallWeight;

        // ── Bayesian shrinkage (prevent extreme values from small samples) ──
        homeScored = BayesianAdjust(homeScored, leagueAvg.HomeGoalsAvg, homeMatches.Count);
        homeConceded = BayesianAdjust(homeConceded, leagueAvg.AwayGoalsAvg, homeMatches.Count);
        awayScored = BayesianAdjust(awayScored, leagueAvg.AwayGoalsAvg, awayMatches.Count);
        awayConceded = BayesianAdjust(awayConceded, leagueAvg.HomeGoalsAvg, awayMatches.Count);

        return new TeamSplitStrength
        {
            HomeAttackStrength = homeScored / leagueAvg.HomeGoalsAvg,
            HomeDefenseWeakness = homeConceded / leagueAvg.AwayGoalsAvg,
            AwayAttackStrength = awayScored / leagueAvg.AwayGoalsAvg,
            AwayDefenseWeakness = awayConceded / leagueAvg.HomeGoalsAvg
        };
    }

    // =============================
    // OUTCOME CALCULATION
    // =============================

    private (double homeWin, double draw, double awayWin, double over25, double btts, double goals23)
        CalculateOutcomes(double lambdaHome, double lambdaAway)
    {
        var matrix = BuildScoreMatrix(lambdaHome, lambdaAway, 6);

        double homeWin = 0, draw = 0, awayWin = 0, goals23 = 0;

        for (var h = 0; h < matrix.GetLength(0); h++)
        for (var a = 0; a < matrix.GetLength(1); a++)
        {
            var p = matrix[h, a];

            if (h > a) homeWin += p;
            else if (h < a) awayWin += p;
            else draw += p;

            if (h + a == 2 || h + a == 3)
                goals23 += p;
        }

        var total = homeWin + draw + awayWin;

        homeWin /= total;
        draw /= total;
        awayWin /= total;
        goals23 /= total;

        // ===== TRUE MARKET FORMULAS =====

        var btts =
            (1 - Math.Exp(-lambdaHome)) *
            (1 - Math.Exp(-lambdaAway));

        var lambdaTotal = lambdaHome + lambdaAway;

        var p0 = Math.Exp(-lambdaTotal);
        var p1 = lambdaTotal * p0;
        var p2 = (lambdaTotal * lambdaTotal / 2) * p0;

        var over25 = 1 - (p0 + p1 + p2);

        return (homeWin, draw, awayWin, over25, btts, goals23);
    }

    // =============================
    // SCORE MATRIX
    // =============================

    private double[,] BuildScoreMatrix(double lambdaHome, double lambdaAway, int maxGoals)
    {
        var matrix = new double[maxGoals + 1, maxGoals + 1];

        var homeProb = PoissonDistribution(lambdaHome, maxGoals);
        var awayProb = PoissonDistribution(lambdaAway, maxGoals);

        for (var h = 0; h <= maxGoals; h++)
        for (var a = 0; a <= maxGoals; a++)
        {
            var p = homeProb[h] * awayProb[a];

            if (h <= 1 && a <= 1)
                p *= DixonColesAdjustment(h, a, lambdaHome, lambdaAway);

            matrix[h, a] = p;
        }

        return matrix;
    }

    // =============================
    // FAST POISSON DISTRIBUTION
    // =============================

    private static double[] PoissonDistribution(double lambda, int maxGoals)
    {
        var probs = new double[maxGoals + 1];
        probs[0] = Math.Exp(-lambda);

        for (var k = 1; k <= maxGoals; k++)
            probs[k] = probs[k - 1] * lambda / k;

        return probs;
    }

    // =============================
    // DIXON COLES
    // =============================

    private static double DixonColesAdjustment(int h, int a, double lH, double lA)
    {
        return (h, a) switch
        {
            (0, 0) => 1 - lH * lA * DixonColesRho,
            (0, 1) => 1 + lH * DixonColesRho,
            (1, 0) => 1 + lA * DixonColesRho,
            (1, 1) => 1 - DixonColesRho,
            _ => 1.0
        };
    }
    // =============================
    // RECENCY WEIGHTING
    // =============================

    /// <summary>
    /// Weight recent matches more heavily using exponential decay.
    /// Index 0 = most recent match → highest weight.
    /// </summary>
    private static double RecencyWeightedAverage(List<double> values, double decay = RecencyDecay)
    {
        if (values.Count == 0) return 0;

        double sum = 0, weightSum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            var w = Math.Pow(decay, i);
            sum += values[i] * w;
            weightSum += w;
        }

        return sum / weightSum;
    }

    // =============================
    // BAYESIAN SHRINKAGE
    // =============================

    /// <summary>
    /// Shrink team average toward league average to prevent extreme
    /// λ values from small sample sizes.
    /// </summary>
    private static double BayesianAdjust(
        double teamAvg, double leagueAvg, int matches,
        int priorStrength = BayesianPriorStrength)
    {
        return (teamAvg * matches + leagueAvg * priorStrength)
               / (matches + priorStrength);
    }
}

public class TeamSplitStrength
{
    public double HomeAttackStrength { get; init; }
    public double HomeDefenseWeakness { get; init; }
    public double AwayAttackStrength { get; init; }
    public double AwayDefenseWeakness { get; init; }
}
