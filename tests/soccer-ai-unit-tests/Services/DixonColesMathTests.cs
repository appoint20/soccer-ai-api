using FluentAssertions;
using SoccerAi.Application.Services;

namespace soccer_ai_unit_tests.Services;

public class DixonColesMathTests
{
    private const double DefaultRho = -0.13;
    private const int DefaultMaxGoals = 8;

    // ── Matrix invariants ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, 1.0, -0.13)]
    [InlineData(1.8, 1.1, -0.13)]
    [InlineData(0.2, 4.5, -0.13)]
    [InlineData(2.5, 0.7, 0.0)]
    [InlineData(1.3, 1.3, 0.1)]
    public void BuildScoreMatrix_SumsToOne(double lambdaHome, double lambdaAway, double rho)
    {
        var matrix = DixonColesMath.BuildScoreMatrix(lambdaHome, lambdaAway, rho, DefaultMaxGoals);

        double sum = 0;
        for (var h = 0; h < matrix.GetLength(0); h++)
        for (var a = 0; a < matrix.GetLength(1); a++)
            sum += matrix[h, a];

        sum.Should().BeApproximately(1.0, 1e-12, "the matrix is explicitly renormalized");
    }

    [Fact]
    public void BuildScoreMatrix_AllCellsNonNegative()
    {
        var matrix = DixonColesMath.BuildScoreMatrix(4.5, 4.5, -0.13, DefaultMaxGoals);

        for (var h = 0; h < matrix.GetLength(0); h++)
        for (var a = 0; a < matrix.GetLength(1); a++)
            matrix[h, a].Should().BeGreaterThanOrEqualTo(0);
    }

    // ── τ formulas: Dixon & Coles (1997) ────────────────────────────────────

    [Fact]
    public void Tau_MatchesDixonColes1997Formulas()
    {
        const double lambda = 1.5; // home mean
        const double mu = 1.2;     // away mean
        const double rho = -0.13;

        // τ(0,0) = 1 − λμρ
        DixonColesMath.Tau(0, 0, lambda, mu, rho).Should().BeApproximately(1.234, 1e-9);
        // τ(0,1) = 1 + λρ
        DixonColesMath.Tau(0, 1, lambda, mu, rho).Should().BeApproximately(0.805, 1e-9);
        // τ(1,0) = 1 + μρ
        DixonColesMath.Tau(1, 0, lambda, mu, rho).Should().BeApproximately(0.844, 1e-9);
        // τ(1,1) = 1 − ρ
        DixonColesMath.Tau(1, 1, lambda, mu, rho).Should().BeApproximately(1.13, 1e-9);
        // all other scores unadjusted
        DixonColesMath.Tau(2, 2, lambda, mu, rho).Should().Be(1.0);
        DixonColesMath.Tau(0, 2, lambda, mu, rho).Should().Be(1.0);
        DixonColesMath.Tau(3, 1, lambda, mu, rho).Should().Be(1.0);
    }

    [Fact]
    public void Tau_WithZeroRho_IsAlwaysOne()
    {
        for (var h = 0; h <= 1; h++)
        for (var a = 0; a <= 1; a++)
            DixonColesMath.Tau(h, a, 1.7, 0.9, 0.0).Should().Be(1.0);
    }

    [Fact]
    public void NegativeRho_IncreasesDrawProbability()
    {
        var independent = DixonColesMath.ComputeMarkets(
            DixonColesMath.BuildScoreMatrix(1.2, 1.0, 0.0, DefaultMaxGoals));
        var dcAdjusted = DixonColesMath.ComputeMarkets(
            DixonColesMath.BuildScoreMatrix(1.2, 1.0, DefaultRho, DefaultMaxGoals));

        dcAdjusted.Draw.Should().BeGreaterThan(independent.Draw,
            "negative ρ inflates low-scoring draws (0-0, 1-1)");
    }

    // ── Markets are consistent with the matrix they came from ───────────────

    [Fact]
    public void ComputeMarkets_IsConsistentWithMatrixCells()
    {
        var matrix = DixonColesMath.BuildScoreMatrix(1.8, 1.1, DefaultRho, DefaultMaxGoals);
        var markets = DixonColesMath.ComputeMarkets(matrix);

        double homeWin = 0, draw = 0, awayWin = 0, over25 = 0, btts = 0, goals23 = 0;
        for (var h = 0; h <= DefaultMaxGoals; h++)
        for (var a = 0; a <= DefaultMaxGoals; a++)
        {
            var p = matrix[h, a];
            if (h > a) homeWin += p; else if (h < a) awayWin += p; else draw += p;
            if (h + a >= 3) over25 += p;
            if (h + a is 2 or 3) goals23 += p;
            if (h >= 1 && a >= 1) btts += p;
        }

        markets.HomeWin.Should().BeApproximately(homeWin, 1e-12);
        markets.Draw.Should().BeApproximately(draw, 1e-12);
        markets.AwayWin.Should().BeApproximately(awayWin, 1e-12);
        markets.Over25.Should().BeApproximately(over25, 1e-12);
        markets.Btts.Should().BeApproximately(btts, 1e-12);
        markets.TwoToThreeGoals.Should().BeApproximately(goals23, 1e-12);

        (markets.HomeWin + markets.Draw + markets.AwayWin)
            .Should().BeApproximately(1.0, 1e-12, "1X2 must exhaust the matrix");
    }

    // ── Known λ pairs give closed-form expected outputs (ρ = 0) ─────────────

    [Fact]
    public void KnownLambdas_UnitMeans_MatchClosedFormPoissonValues()
    {
        // With ρ=0 and λH=λA=1 the model is plain independent Poisson:
        //   P(over 2.5) = 1 − e⁻²(1 + 2 + 2)        = 0.323324
        //   P(btts)     = (1 − e⁻¹)²                = 0.399576
        //   P(draw)     = e⁻² Σ 1/(k!)²             = 0.308508
        //   P(2-3 goals)= e⁻²(2 + 8/6)              = 0.451118
        var markets = DixonColesMath.ComputeMarkets(
            DixonColesMath.BuildScoreMatrix(1.0, 1.0, 0.0, DefaultMaxGoals));

        markets.Over25.Should().BeApproximately(0.323324, 1e-4);
        markets.Btts.Should().BeApproximately(0.399576, 1e-4);
        markets.Draw.Should().BeApproximately(0.308508, 1e-4);
        markets.TwoToThreeGoals.Should().BeApproximately(0.451118, 1e-4);
        markets.HomeWin.Should().BeApproximately(markets.AwayWin, 1e-9,
            "equal λ must give symmetric win probabilities");
    }

    [Fact]
    public void HigherHomeLambda_ShiftsProbabilityTowardHomeWin()
    {
        var markets = DixonColesMath.ComputeMarkets(
            DixonColesMath.BuildScoreMatrix(2.4, 0.8, DefaultRho, DefaultMaxGoals));

        markets.HomeWin.Should().BeGreaterThan(markets.AwayWin);
        markets.HomeWin.Should().BeGreaterThan(markets.Draw);
    }

    [Fact]
    public void PoissonPmf_MatchesClosedForm()
    {
        var pmf = DixonColesMath.PoissonPmf(1.5, DefaultMaxGoals);

        pmf[0].Should().BeApproximately(Math.Exp(-1.5), 1e-12);
        pmf[1].Should().BeApproximately(1.5 * Math.Exp(-1.5), 1e-12);
        pmf[2].Should().BeApproximately(Math.Pow(1.5, 2) / 2 * Math.Exp(-1.5), 1e-12);
        pmf[3].Should().BeApproximately(Math.Pow(1.5, 3) / 6 * Math.Exp(-1.5), 1e-12);
    }
}
