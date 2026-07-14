namespace SoccerAi.Application.Services;

/// <summary>
/// Pure Dixon-Coles math (Dixon &amp; Coles, 1997). Stateless and fully unit-testable.
/// Every market probability is derived from the SAME renormalized,
/// DC-adjusted score matrix — no independent side formulas.
/// </summary>
public static class DixonColesMath
{
    /// <summary>Poisson PMF for k = 0..maxGoals.</summary>
    public static double[] PoissonPmf(double lambda, int maxGoals)
    {
        var probs = new double[maxGoals + 1];
        probs[0] = Math.Exp(-lambda);
        for (var k = 1; k <= maxGoals; k++)
            probs[k] = probs[k - 1] * lambda / k;
        return probs;
    }

    /// <summary>
    /// Dixon-Coles low-score correction τ(h, a) exactly as in the 1997 paper:
    /// τ(0,0)=1−λμρ, τ(0,1)=1+λρ, τ(1,0)=1+μρ, τ(1,1)=1−ρ, else 1.
    /// λ = home mean, μ = away mean.
    /// </summary>
    public static double Tau(int homeGoals, int awayGoals, double lambdaHome, double lambdaAway, double rho)
    {
        return (homeGoals, awayGoals) switch
        {
            (0, 0) => 1 - lambdaHome * lambdaAway * rho,
            (0, 1) => 1 + lambdaHome * rho,
            (1, 0) => 1 + lambdaAway * rho,
            (1, 1) => 1 - rho,
            _ => 1.0
        };
    }

    /// <summary>
    /// Builds the DC-adjusted score matrix and renormalizes it so all cells
    /// sum to exactly 1 (fixes both the τ distortion and the truncation loss).
    /// </summary>
    public static double[,] BuildScoreMatrix(double lambdaHome, double lambdaAway, double rho, int maxGoals)
    {
        var matrix = new double[maxGoals + 1, maxGoals + 1];
        var homePmf = PoissonPmf(lambdaHome, maxGoals);
        var awayPmf = PoissonPmf(lambdaAway, maxGoals);

        double total = 0;
        for (var h = 0; h <= maxGoals; h++)
        for (var a = 0; a <= maxGoals; a++)
        {
            var p = homePmf[h] * awayPmf[a] * Tau(h, a, lambdaHome, lambdaAway, rho);
            p = Math.Max(0, p); // τ can go negative for extreme ρ/λ combinations
            matrix[h, a] = p;
            total += p;
        }

        if (total <= 0)
            throw new InvalidOperationException(
                $"Degenerate score matrix (total={total}) for λH={lambdaHome}, λA={lambdaAway}, ρ={rho}.");

        for (var h = 0; h <= maxGoals; h++)
        for (var a = 0; a <= maxGoals; a++)
            matrix[h, a] /= total;

        return matrix;
    }

    /// <summary>
    /// Derives ALL market probabilities from one renormalized score matrix.
    /// </summary>
    public static MatrixMarkets ComputeMarkets(double[,] matrix)
    {
        double homeWin = 0, draw = 0, awayWin = 0, over25 = 0, btts = 0, goals23 = 0;

        var maxH = matrix.GetLength(0) - 1;
        var maxA = matrix.GetLength(1) - 1;

        for (var h = 0; h <= maxH; h++)
        for (var a = 0; a <= maxA; a++)
        {
            var p = matrix[h, a];

            if (h > a) homeWin += p;
            else if (h < a) awayWin += p;
            else draw += p;

            var totalGoals = h + a;
            if (totalGoals >= 3) over25 += p;
            if (totalGoals is 2 or 3) goals23 += p;
            if (h >= 1 && a >= 1) btts += p;
        }

        return new MatrixMarkets(homeWin, draw, awayWin, over25, btts, goals23);
    }
}

/// <summary>Market probabilities derived from a single score matrix.</summary>
public readonly record struct MatrixMarkets(
    double HomeWin,
    double Draw,
    double AwayWin,
    double Over25,
    double Btts,
    double TwoToThreeGoals);
