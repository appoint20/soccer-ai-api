using System;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services
{
    public class PoissonGoalModelService : IPoissonGoalModelService
    {
        private const int MaxGoals = 5;

        public PredictionResult PredictMatch(
            double homeAttackRating,
            double homeDefenseRating,
            double awayAttackRating,
            double awayDefenseRating,
            double leagueAvgGoals,
            double homeAdvantageFactor,
            double rho)
        {
            // 1. Compute Expected Goals (Lambda)
            var lambdaHome = leagueAvgGoals * homeAttackRating * awayDefenseRating * homeAdvantageFactor;
            var lambdaAway = leagueAvgGoals * awayAttackRating * homeDefenseRating;

            // 2. Generate Score Probability Matrix (Poisson)
            var matrix = new double[MaxGoals + 1, MaxGoals + 1];
            
            for (int h = 0; h <= MaxGoals; h++)
            {
                var probHome = PoissonPmf(h, lambdaHome);
                for (int a = 0; a <= MaxGoals; a++)
                {
                    var probAway = PoissonPmf(a, lambdaAway);
                    matrix[h, a] = probHome * probAway;
                }
            }

            // 3. Apply Dixon-Coles Correction
            if (Math.Abs(rho) > 0.0001)
            {
                ApplyDixonColesCorrection(matrix, lambdaHome, lambdaAway, rho);
            }

            // 4. Normalize Matrix
            NormalizeMatrix(matrix);

            // 5. Derive Market Probabilities
            var markets = CalculateMarketProbabilities(matrix);

            return new PredictionResult
            {
                ExpectedGoalsHome = lambdaHome,
                ExpectedGoalsAway = lambdaAway,
                HomeWinProbability = markets.HomeWin,
                DrawProbability = markets.Draw,
                AwayWinProbability = markets.AwayWin,
                Over25Probability = markets.Over25,
                BttsProbability = markets.Btts,
                ScoreMatrix = matrix
            };
        }

        private double PoissonPmf(int k, double lambda)
        {
            // Formula: (lambda^k * e^-lambda) / k!
            return Math.Pow(lambda, k) * Math.Exp(-lambda) / Factorial(k);
        }

        private long Factorial(int n)
        {
            if (n <= 1) return 1;
            long result = 1;
            for (int i = 2; i <= n; i++) result *= i;
            return result;
        }

        private void ApplyDixonColesCorrection(double[,] matrix, double lambdaHome, double lambdaAway, double rho)
        {
            // Correction factors for 0-0, 0-1, 1-0, 1-1
            
            // 0-0
            matrix[0, 0] *= (1.0 - lambdaHome * lambdaAway * rho);

            // 0-1 (Home 0, Away 1)
            if (matrix.GetLength(1) > 1)
                matrix[0, 1] *= (1.0 + lambdaHome * rho);

            // 1-0 (Home 1, Away 0)
            if (matrix.GetLength(0) > 1)
                matrix[1, 0] *= (1.0 + lambdaAway * rho);

            // 1-1
            if (matrix.GetLength(0) > 1 && matrix.GetLength(1) > 1)
                matrix[1, 1] *= (1.0 - rho);
        }

        private void NormalizeMatrix(double[,] matrix)
        {
            double sum = 0;
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    sum += matrix[i, j];

            if (sum > 0)
            {
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++)
                        matrix[i, j] /= sum;
            }
        }

        private (double HomeWin, double Draw, double AwayWin, double Over25, double Btts) CalculateMarketProbabilities(double[,] matrix)
        {
            double homeWin = 0;
            double draw = 0;
            double awayWin = 0;
            double over25 = 0;
            double btts = 0;

            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            for (int h = 0; h < rows; h++)
            {
                for (int a = 0; a < cols; a++)
                {
                    double prob = matrix[h, a];
                    int totalGoals = h + a;

                    // 1X2
                    if (h > a) homeWin += prob;
                    else if (h == a) draw += prob;
                    else awayWin += prob;

                    // Over 2.5
                    if (totalGoals > 2.5) over25 += prob;

                    // BTTS
                    if (h > 0 && a > 0) btts += prob;
                }
            }

            return (homeWin, draw, awayWin, over25, btts);
        }
    }
}
